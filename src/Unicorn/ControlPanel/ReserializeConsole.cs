﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Web;
using Kamsar.WebConsole;
using Sitecore.StringExtensions;
using Unicorn.Configuration;
using Unicorn.ControlPanel.Headings;
using Unicorn.Data;
using Unicorn.Logging;
using Unicorn.Predicates;

namespace Unicorn.ControlPanel
{
	/// <summary>
	/// Renders a WebConsole that handles reserialize - or initial serialize - for Unicorn configurations
	/// </summary>
	public class ReserializeConsole : ControlPanelConsole
	{
		private readonly IConfiguration[] _configurations;

		public ReserializeConsole(bool isAutomatedTool, IConfiguration[] configurations)
			: base(isAutomatedTool, new HeadingService())
		{
			_configurations = configurations;
		}

		protected override string Title
		{
			get { return "Reserialize Unicorn"; }
		}

		protected override void Process(IProgressStatus progress)
		{
			foreach (var configuration in ResolveConfigurations())
			{
				var logger = configuration.Resolve<ILogger>();

				using (new LoggingContext(new WebConsoleLogger(progress), configuration))
				{
					try
					{
						var timer = new Stopwatch();
						timer.Start();

						logger.Info(configuration.Name + " is being reserialized");

						using (new TransparentSyncDisabler())
						{
							var targetDataStore = configuration.Resolve<ITargetDataStore>();
							var helper = configuration.Resolve<SerializationHelper>();

							// nuke any existing items in the store before we begin. This is a full reserialize so we want to
							// get rid of any existing stuff even if it's not part of existing configs
							logger.Warn("[D] Clearing existing items from {0}".FormatWith(targetDataStore.FriendlyName));
							targetDataStore.Clear();

							var roots = configuration.Resolve<PredicateRootPathResolver>().GetRootSourceItems();

							int index = 1;
							foreach (var root in roots)
							{
								helper.DumpTree(root, configuration);
								progress.Report((int) ((index/(double) roots.Length)*100));
								index++;
							}
						}

						timer.Stop();

						logger.Info("{0} reserialization complete in {1}ms".FormatWith(configuration.Name, timer.ElapsedMilliseconds));
					}
					catch (Exception ex)
					{
						logger.Error(ex);
						break;
					}
				}
			}
		}

		protected virtual IConfiguration[] ResolveConfigurations()
		{
			var config = HttpContext.Current.Request.QueryString["configuration"];

			if (string.IsNullOrWhiteSpace(config)) return _configurations;

			var targetConfiguration = _configurations.FirstOrDefault(x => x.Name == config);

			if (targetConfiguration == null) throw new ArgumentException("Configuration requested was not defined.");

			return new[] { targetConfiguration };
		}
	}
}
