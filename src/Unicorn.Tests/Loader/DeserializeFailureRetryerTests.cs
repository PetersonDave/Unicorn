﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NSubstitute;
using Rainbow.Model;
using Rainbow.Tests;
using Unicorn.Data;
using Unicorn.Loader;
using Xunit;

namespace Unicorn.Tests.Loader
{
	public class DeserializeFailureRetryerTests
	{
		[Fact]
		public void ShouldRetrieveItemRetry()
		{
			var retryer = new DeserializeFailureRetryer();
			var item = new FakeItem();
			var exception = new Exception();
			retryer.AddItemRetry(item, exception);

			var callback = Substitute.For<Action<IItemData>>();

			retryer.RetryAll(Substitute.For<ISourceDataStore>(), callback, callback);

			callback.Received()(item);
		}

		[Fact]
		public void ShouldRetrieveTreeRetry()
		{
			var retryer = new DeserializeFailureRetryer();
			var item = new FakeItem();
			var exception = new Exception();
			retryer.AddTreeRetry(item, exception);

			var callback = Substitute.For<Action<IItemData>>();

			retryer.RetryAll(Substitute.For<ISourceDataStore>(), callback, callback);

			callback.Received()(item);
		}

		[Fact]
		public void ShouldThrowIfItemRetryFails()
		{
			var retryer = new DeserializeFailureRetryer();
			var item = new FakeItem();
			var exception = new Exception();

			retryer.AddItemRetry(item, exception);

			Action<IItemData> callback = delegate(IItemData x) { throw new Exception(); };

			Assert.Throws<DeserializationAggregateException>(() => retryer.RetryAll(Substitute.For<ISourceDataStore>(), callback, callback));
		}

		[Fact]
		public void ShouldThrowIfTreeRetryFails()
		{
			var retryer = new DeserializeFailureRetryer();
			var item = new FakeItem();
			var exception = new Exception();

			retryer.AddTreeRetry(item, exception);

			Action<IItemData> callback = delegate (IItemData x) { throw new Exception(); };

			Assert.Throws<DeserializationAggregateException>(() => retryer.RetryAll(Substitute.For<ISourceDataStore>(), callback, callback));
		}
	}
}
