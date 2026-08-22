using System.Text.Json.Nodes;

namespace Bustrap.Models.Persistable
{
    /// <summary>
    /// Implemented by persisted settings that have had properties renamed.
    /// <see cref="JsonManager{T}"/> calls <see cref="Migrate"/> straight after
    /// deserialising, handing over the raw document so values stored under the
    /// old names can be picked up instead of silently falling back to defaults.
    /// </summary>
    public interface IMigratableSettings
    {
        /// <param name="raw">The document as it was read from disk.</param>
        /// <returns>
        /// <c>true</c> if anything was carried over, meaning the file should be
        /// rewritten so the legacy keys are dropped.
        /// </returns>
        bool Migrate(JsonObject raw);
    }
}
