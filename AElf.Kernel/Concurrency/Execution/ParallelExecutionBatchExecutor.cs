﻿using System;
using System.Linq;
using System.Collections.Generic;
using Akka.Actor;
using AElf.Kernel.Concurrency.Execution.Messages;
using AElf.Kernel.Concurrency;

namespace AElf.Kernel.Concurrency.Execution
{
	/// <summary>
	/// Batch executor groups a list of transactions into jobs and run them in parallel.
	/// </summary>
	public class ParallelExecutionBatchExecutor : UntypedActor
	{
		enum State
		{
			PendingGrouping,
			ReadyToRun,
			Running
		}
		private State _state = State.PendingGrouping;
		private bool _startExecutionMessageReceived = false;
		private Grouper _grouper = new Grouper();
		private IChainContext _chainContext;
		private List<Transaction> _transactions;
		private List<List<Transaction>> _grouped;
		private IActorRef _resultCollector;
		private Dictionary<IActorRef, List<Transaction>> _actorToTransactions = new Dictionary<IActorRef, List<Transaction>>();
		private Dictionary<Hash, TransactionResult> _transactionResults = new Dictionary<Hash, TransactionResult>();

		public ParallelExecutionBatchExecutor(IChainContext chainContext, List<Transaction> transactions, IActorRef resultCollector)
		{
			_chainContext = chainContext;
			_transactions = transactions;
			_resultCollector = resultCollector;
		}

		protected override void PreStart()
		{
			Context.System.Scheduler.ScheduleTellOnce(new TimeSpan(0, 0, 0), Self, new StartGroupingMessage(), Self);
		}

		protected override void OnReceive(object message)
		{
			switch (message)
			{
				case StartGroupingMessage startGrouping:
					if (_state == State.PendingGrouping)
					{
						_grouped = _grouper.Process(_transactions);
						// TODO: Report and/or log grouping outcomes
						CreateChildren();
						_state = State.ReadyToRun;
						MaybeStartChildren();
					}
					break;
				case StartExecutionMessage start:
					_startExecutionMessageReceived = true;
					MaybeStartChildren();
					break;
				case TransactionResultMessage res:
					_transactionResults[res.TransactionResult.TransactionId] = res.TransactionResult;
					ForwardResult(res);
					StopIfAllFinished();
					break;
				case Terminated t:
					// For now, just ignore
					// TODO: Handle failure
					break;
			}
		}

		private void CreateChildren()
		{
			foreach (var txs in _grouped)
			{
				var actor = Context.ActorOf(ParallelExecutionJobExecutor.Props(_chainContext, txs, Self));
				_actorToTransactions.Add(actor, txs);
			}
		}

		private void MaybeStartChildren()
		{
			if (_state == State.ReadyToRun && _startExecutionMessageReceived)
			{
				foreach (var a in _actorToTransactions.Keys)
				{
					a.Tell(new StartExecutionMessage());
				}
				_state = State.Running;
			}
		}

		private void ForwardResult(TransactionResultMessage resultMessage)
		{
			if (_resultCollector != null)
			{
				_resultCollector.Forward(resultMessage);
			}
		}

		private void StopIfAllFinished()
		{
			if (_transactionResults.Count == _transactions.Count)
			{
				Context.Stop(Self);
			}
		}

		public static Props Props(IChainContext chainContext, List<Transaction> job, IActorRef resultCollector)
		{
			return Akka.Actor.Props.Create(() => new ParallelExecutionBatchExecutor(chainContext, job, resultCollector));
		}
	}
}
