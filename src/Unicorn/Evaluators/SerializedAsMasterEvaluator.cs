﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Rainbow.Filtering;
using Rainbow.Formatting.FieldFormatters;
using Rainbow.Model;
using Rainbow.Predicates;
using Rainbow.Storage.Sc.Deserialization;
using Sitecore.Configuration;
using Sitecore.Diagnostics;
using Unicorn.ControlPanel;
using Unicorn.Data;
using Unicorn.Evaluators.Comparison;
using Unicorn.Logging;

namespace Unicorn.Evaluators
{
	/// <summary>
	/// Evaluates to overwrite the source data if ANY differences exist in the serialized version.
	/// </summary>
	public class SerializedAsMasterEvaluator : IEvaluator, IDocumentable
	{
		private readonly ISerializedAsMasterEvaluatorLogger _logger;
		private readonly IFieldFilter _fieldFilter;
		private readonly ISourceDataStore _sourceDataStore;
		private readonly IDeserializer _deserializer;
		protected static readonly Guid RootId = new Guid("{11111111-1111-1111-1111-111111111111}");
		protected readonly List<IFieldComparer> FieldComparers = new List<IFieldComparer>(); 

		public SerializedAsMasterEvaluator(XmlNode configNode, ISerializedAsMasterEvaluatorLogger logger, IFieldFilter fieldFilter, ISourceDataStore sourceDataStore, IDeserializer deserializer) :  this(logger, fieldFilter, sourceDataStore, deserializer)
		{
			Assert.ArgumentNotNull(configNode, "configNode");

			_fieldFilter = fieldFilter;

			var comparers = configNode.ChildNodes;

			foreach (XmlNode comparer in comparers)
			{
				if (comparer.NodeType == XmlNodeType.Element && comparer.Name.Equals("fieldComparer")) 
					FieldComparers.Add(Factory.CreateObject<IFieldComparer>(comparer));
			}

			FieldComparers.Add(new DefaultComparison());
		}

		protected SerializedAsMasterEvaluator(ISerializedAsMasterEvaluatorLogger logger, IFieldFilter fieldFilter, ISourceDataStore sourceDataStore, IDeserializer deserializer)
		{
			Assert.ArgumentNotNull(logger, "logger");
			Assert.ArgumentNotNull(fieldFilter, "fieldFilter");
			Assert.ArgumentNotNull(fieldFilter, "fieldPredicate");

			_logger = logger;
			_fieldFilter = fieldFilter;
			_sourceDataStore = sourceDataStore;
			_deserializer = deserializer;
		}

		public void EvaluateOrphans(ISerializableItem[] orphanItems)
		{
			Assert.ArgumentNotNull(orphanItems, "orphanItems");

			EvaluatorUtility.RecycleItems(orphanItems, _sourceDataStore, item => _logger.DeletedItem(item));
		}

		public ISerializableItem EvaluateNewSerializedItem(ISerializableItem newItem)
		{
			Assert.ArgumentNotNull(newItem, "newItem");

			_logger.DeserializedNewItem(newItem);

			var updatedItem = DoDeserialization(newItem);

			return updatedItem;
		}

		public ISerializableItem EvaluateUpdate(ISerializableItem serializedItem, ISerializableItem existingItem)
		{
			Assert.ArgumentNotNull(serializedItem, "serializedItem");
			Assert.ArgumentNotNull(existingItem, "existingItem");

			var deferredUpdateLog = new DeferredLogWriter<ISerializedAsMasterEvaluatorLogger>();

			if (ShouldUpdateExisting(serializedItem, existingItem, deferredUpdateLog))
			{
				_logger.SerializedUpdatedItem(serializedItem);

				deferredUpdateLog.ExecuteDeferredActions(_logger);

				var updatedItem = DoDeserialization(serializedItem);

				return updatedItem;
			}

			return null;
		}

		protected virtual bool ShouldUpdateExisting(ISerializableItem serializedItem, ISerializableItem existingItem, DeferredLogWriter<ISerializedAsMasterEvaluatorLogger> deferredUpdateLog)
		{
			Assert.ArgumentNotNull(serializedItem, "serializedItem");
			Assert.ArgumentNotNull(existingItem, "existingItem");

			if (existingItem.Id == RootId) return false; // we never want to update the Sitecore root item

			// check if templates are different
			if (IsTemplateMatch(existingItem, serializedItem, deferredUpdateLog)) return true;

			// check if names are different
			if (IsNameMatch(existingItem, serializedItem, deferredUpdateLog)) return true;

			// check if source has version(s) that serialized does not
			var orphanVersions = existingItem.Versions.Where(sourceVersion => serializedItem.GetVersion(sourceVersion.Language.Name, sourceVersion.VersionNumber) == null).ToArray();
			if (orphanVersions.Length > 0)
			{
				deferredUpdateLog.AddEntry(x => x.OrphanSourceVersion(existingItem, serializedItem, orphanVersions));
				return true; // source contained versions not present in the serialized version, which is a difference
			}

			// check if shared fields have any mismatching values
			if (AnyFieldMatch(serializedItem.SharedFields, existingItem.SharedFields, existingItem, serializedItem, deferredUpdateLog))
				return true;

			// see if the serialized versions have any mismatching values in the source data
			return serializedItem.Versions.Any(serializedeVersion =>
			{
				var sourceISerializableVersion = existingItem.GetVersion(serializedeVersion.Language.Name, serializedeVersion.VersionNumber);

				// version exists in serialized item but does not in source version
				if (sourceISerializableVersion == null)
				{
					deferredUpdateLog.AddEntry(x => x.NewSerializedVersionMatch(serializedeVersion, serializedItem, existingItem));
					return true;
				}

				// field values mismatch
				var fieldMatch = AnyFieldMatch(serializedeVersion.Fields, sourceISerializableVersion.Fields, existingItem, serializedItem, deferredUpdateLog, serializedeVersion);
				if (fieldMatch) return true;

				// if we get here everything matches to the best of our knowledge, so we return false (e.g. "do not update this item")
				return false;
			});
		}

		protected virtual bool IsNameMatch(ISerializableItem existingItem, ISerializableItem serializedItem, DeferredLogWriter<ISerializedAsMasterEvaluatorLogger> deferredUpdateLog)
		{
			if (!serializedItem.Name.Equals(existingItem.Name))
			{
				deferredUpdateLog.AddEntry(x => x.IsNameMatch(serializedItem, existingItem));

				return true;
			}

			return false;
		}

		protected virtual bool IsTemplateMatch(ISerializableItem existingItem, ISerializableItem serializedItem, DeferredLogWriter<ISerializedAsMasterEvaluatorLogger> deferredUpdateLog)
		{
			if (existingItem.TemplateId == default(Guid) && serializedItem.TemplateId == default(Guid)) return false;

			bool match = !serializedItem.TemplateId.Equals(existingItem.TemplateId);
			if(match)
				deferredUpdateLog.AddEntry(x=>x.IsTemplateMatch(serializedItem, existingItem));

			return match;
		}

		protected virtual bool AnyFieldMatch(IEnumerable<ISerializableFieldValue> sourceFields, IEnumerable<ISerializableFieldValue> targetFields, ISerializableItem existingItem, ISerializableItem serializedItem, DeferredLogWriter<ISerializedAsMasterEvaluatorLogger> deferredUpdateLog, ISerializableVersion version = null)
		{
			if (sourceFields == null) return false;
			var targetFieldIndex = targetFields.ToDictionary(x => x.FieldId);

			return sourceFields.Any(sourceField =>
			{
				if (!_fieldFilter.Includes(sourceField.FieldId)) return false;

				if (!sourceField.IsFieldComparable()) return false;

				bool isMatch = IsFieldMatch(sourceField, targetFieldIndex, sourceField.FieldId);
				if(isMatch) deferredUpdateLog.AddEntry(logger =>
				{
					ISerializableFieldValue sourceFieldValue;
					if (targetFieldIndex.TryGetValue(sourceField.FieldId, out sourceFieldValue))
					{
						if (version == null) logger.IsSharedFieldMatch(serializedItem, sourceField.FieldId, sourceField.Value, sourceFieldValue.Value);
						else logger.IsVersionedFieldMatch(serializedItem, version, sourceField.FieldId, sourceField.Value, sourceFieldValue.Value);
					}
				});
				return isMatch;
			});
		}

		protected virtual bool IsFieldMatch(ISerializableFieldValue sourceField, Dictionary<Guid, ISerializableFieldValue> targetFields, Guid fieldId)
		{
			// note that returning "true" means the values DO NOT MATCH EACH OTHER.

			if (sourceField == null) return false;

			// it's a "match" if the target item does not contain the source field
			ISerializableFieldValue targetField;
			if (!targetFields.TryGetValue(fieldId, out targetField)) return true;

			var fieldComparer = FieldComparers.FirstOrDefault(comparer => comparer.CanCompare(sourceField, targetField));

			if(fieldComparer == null) throw new InvalidOperationException("Unable to find a field comparison for " + sourceField.NameHint);

			return !fieldComparer.AreEqual(sourceField, targetField);
		}

		protected virtual ISerializableItem DoDeserialization(ISerializableItem serializedItem)
		{
			ISerializableItem updatedItem = _deserializer.Deserialize(serializedItem, false);

			Assert.IsNotNull(updatedItem, "Do not return null from DeserializeItem() - throw an exception if an error occurs.");

			return updatedItem;
		}

		public string FriendlyName
		{
			get { return "Serialized as Master Evaluator"; }
		}

		public string Description
		{
			get { return "Treats the items that are serialized as the master copy, and any changes whether newer or older are synced into the source data. This allows for all merging to occur in source control, and is the default way Unicorn behaves."; }
		}

		public KeyValuePair<string, string>[] GetConfigurationDetails()
		{
			return null;
		}
	}
}
