﻿using Rainbow.Model;

namespace Unicorn.Evaluators
{
	/// <summary>
	/// Evaluators decide what to do with items when new items, orphans, or comparisons are found
	/// </summary>
	public interface IEvaluator
	{
		/// <summary>
		/// Orphans are items the loader has found that exist in the source data, but not in the serialization store.
		/// This method allows you to decide what to do with them (e.g. delete them, recycle them, serialize them elsewhere then delete, or do nothing)
		/// </summary>
		/// <param name="orphanItems"></param>
		void EvaluateOrphans(IItemData[] orphanItems);

		/// <summary>
		/// If a serialized item is found that does not have a corresponding item in source data, this method is invoked to decide what to do about it.
		/// Normally, this would probably trigger the deserialization of the serialized item into source data.
		/// </summary>
		/// <param name="newItemData">The new serialized item not present in source data</param>
		/// <returns>If a new source item is created, return it. If not, return null.</returns>
		IItemData EvaluateNewSerializedItem(IItemData newItemData);

		/// <summary>
		/// If a serialized item is found that has a corresponding item in source data, this method is invoked to perform any updates that are needed to the source data.
		/// Updating is pretty slow so it's much faster to skip unnecessary updates. Normally this would probably compare timestamps etc and if changed, trigger a deserialization.
		/// </summary>
		/// <param name="sourceItem">The existing item in Sitecore</param>
		/// <param name="targetItem">The serialized item to evaluate</param>
		/// <returns>If an update is performed, return the updated source item. If no update occurs, return null.</returns>
		IItemData EvaluateUpdate(IItemData sourceItem, IItemData targetItem);
	}
}
