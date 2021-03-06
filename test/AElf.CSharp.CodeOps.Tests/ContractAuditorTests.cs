using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AElf.Contracts.Association;
using AElf.Contracts.Configuration;
using AElf.Contracts.Consensus.AEDPoS;
using AElf.Contracts.CrossChain;
using AElf.Contracts.Economic;
using AElf.Contracts.Election;
using AElf.Contracts.Genesis;
using AElf.Contracts.MultiToken;
using AElf.Contracts.Parliament;
using AElf.Contracts.Profit;
using AElf.Contracts.Referendum;
using AElf.Contracts.TestContract.TransactionFees;
using AElf.Contracts.TokenConverter;
using AElf.Contracts.Treasury;
using AElf.CSharp.CodeOps.Validators;
using AElf.CSharp.CodeOps.Validators.Assembly;
using AElf.CSharp.CodeOps.Validators.Method;
using AElf.Runtime.CSharp.Tests.BadContract;
using Shouldly;
using Xunit;

namespace AElf.CSharp.CodeOps
{
    public class ContractAuditorFixture : IDisposable
    {
        private ContractAuditor _auditor;

        public ContractAuditorFixture()
        {
            _auditor = new ContractAuditor(null, null);
        }

        public void Audit(byte[] code)
        {
            _auditor.Audit(code, false);
        }

        public void Dispose()
        {
            _auditor = null;
        }
    }

    public class ContractAuditorTests : CSharpCodeOpsTestBase, IClassFixture<ContractAuditorFixture>
    {
        private readonly ContractAuditorFixture _auditorFixture;
        private readonly string _contractDllDir = "../../../contracts/";

        private readonly Type[] _contracts =
        {
            typeof(AssociationContract),
            typeof(ConfigurationContract),
            typeof(AEDPoSContract),
            typeof(CrossChainContract),
            typeof(EconomicContract),
            typeof(ElectionContract),
            typeof(BasicContractZero),
            typeof(TokenContract),
            typeof(ParliamentContract),
            typeof(ProfitContract),
            typeof(ReferendumContract),
            typeof(TokenConverterContract),
            typeof(TreasuryContract)
        };

        public ContractAuditorTests(ContractAuditorFixture auditorFixture)
        {
            // Use fixture to instantiate auditor only once
            _auditorFixture = auditorFixture;
        }

        #region Positive Cases

        [Fact]
        public void CheckSystemContracts_AllShouldPass()
        {
            // Load the DLL's from contracts folder to prevent codecov injection
            foreach (var contractPath in _contracts.Select(c => _contractDllDir + c.Module + ".patched"))
            {
                Should.NotThrow(()=>_auditorFixture.Audit(ReadCode(contractPath)));
            }
        }

        [Fact]
        public void ContractPatcher_Test()
        {
            const string contract = "AElf.Contracts.MultiToken.dll";
            var code = ReadCode(Path.Combine(_contractDllDir, contract));
            var updateCode = ContractPatcher.Patch(code);
            code.ShouldNotBe(updateCode);
            var exception = Record.Exception(() => _auditorFixture.Audit(updateCode));
            exception.ShouldBeNull();
        }

        #endregion

        #region Negative Cases

        [Fact]
        public void CheckBadContract_ForFindings()
        {
            var findings = Should.Throw<InvalidCodeException>(
                ()=>_auditorFixture.Audit(ReadCode(_contractDllDir + typeof(BadContract).Module)))
                .Findings;
            
            // Should have identified that ACS1 or ACS8 is not there
            findings.FirstOrDefault(f => f is AcsValidationResult).ShouldNotBeNull();
            
            // Random usage
            LookFor(findings,
                    "UpdateStateWithRandom",
                    i => i.Namespace == "System" && i.Type == "Random")
                .ShouldNotBeNull();

            // DateTime UtcNow usage
            LookFor(findings,
                    "UpdateStateWithCurrentTime",
                    i => i.Namespace == "System" && i.Type == "DateTime" && i.Member == "get_UtcNow")
                .ShouldNotBeNull();

            // DateTime Now usage
            LookFor(findings,
                    "UpdateStateWithCurrentTime",
                    i => i.Namespace == "System" && i.Type == "DateTime" && i.Member == "get_Now")
                .ShouldNotBeNull();

            // DateTime Today usage
            LookFor(findings,
                    "UpdateStateWithCurrentTime",
                    i => i.Namespace == "System" && i.Type == "DateTime" && i.Member == "get_Today")
                .ShouldNotBeNull();

            // Double type usage
            LookFor(findings,
                    "UpdateDoubleState",
                    i => i.Namespace == "System" && i.Type == "Double")
                .ShouldNotBeNull();

            // Float type usage
            LookFor(findings,
                    "UpdateFloatState",
                    i => i.Namespace == "System" && i.Type == "Single")
                .ShouldNotBeNull();

            // Disk Ops usage
            LookFor(findings,
                    "WriteFileToNode",
                    i => i.Namespace == "System.IO")
                .ShouldNotBeNull();

            // String constructor usage
            LookFor(findings,
                    "InitLargeStringDynamic",
                    i => i.Namespace == "System" && i.Type == "String" && i.Member == ".ctor")
                .ShouldNotBeNull();

            // Denied member use in nested class
            LookFor(findings,
                    "UseDeniedMemberInNestedClass",
                    i => i.Namespace == "System" && i.Type == "DateTime" && i.Member == "get_Now")
                .ShouldNotBeNull();

            // Denied member use in separate class
            LookFor(findings,
                    "UseDeniedMemberInSeparateClass",
                    i => i.Namespace == "System" && i.Type == "DateTime" && i.Member == "get_Now")
                .ShouldNotBeNull();

            // Large array initialization
            findings.FirstOrDefault(f => f is ArrayValidationResult && f.Info.ReferencingMethod == "InitLargeArray")
                .ShouldNotBeNull();

            // Float operations
            findings.FirstOrDefault(f => f is FloatOpsValidationResult)
                .ShouldNotBeNull();
        }

        [Fact]
        public void CheckPatchAudit_ForUncheckedMathOpcodes()
        {
            // Here, we use any contract that contains unchecked math OpCode even with "Check for arithmetic overflow"
            // checked in the project. If first section of below test case fails, need to create another contract  
            // that iterates an array with foreach loop.
            var contractCode = ReadCode(_contractDllDir + typeof(TransactionFeesContract).Module);
            
            var findings = Should.Throw<InvalidCodeException>(
                    ()=>_auditorFixture.Audit(contractCode))
                .Findings;
            
            findings.FirstOrDefault(f => f is UncheckedMathValidationResult)
                .ShouldNotBeNull();
            
            // After patching, all unchecked arithmetic OpCodes should be cleared.
            Should.NotThrow(() => _auditorFixture.Audit(ContractPatcher.Patch(contractCode)));
        }

        #endregion

        #region Test Helpers

        byte[] ReadCode(string path)
        {
            return File.Exists(path)
                ? File.ReadAllBytes(path)
                : throw new FileNotFoundException("Contract DLL cannot be found. " + path);
        }

        Info LookFor(IEnumerable<ValidationResult> findings, string referencingMethod, Func<Info, bool> criteria)
        {
            return findings.Select(f => f.Info)
                .FirstOrDefault(i => i != null && i.ReferencingMethod == referencingMethod && criteria(i));
        }

        #endregion
    }
}