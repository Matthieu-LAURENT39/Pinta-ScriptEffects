using Mono.Addins;

namespace ScriptEffects;

/// <summary>
/// Utility functions.
/// </summary>
internal static class Utils
{
    /// <summary>
    /// Localises a string using the Addin's localizer.
    /// This should be used for strings that are specific to the add-in.
    /// </summary>
    /// <param name="msgid">The message ID to localise.</param>
    /// <returns>The localised string.</returns>
    public static string L(string msgid) => AddinManager.CurrentLocalizer.GetString(msgid);

    /// <summary>
    /// Localises a string using Pinta's core localizer.
    /// This should be used for strings that are shared with the main application.
    /// </summary> <param name="msgid">The message ID to localise.</param>
    /// <returns>The localised string.</returns>
    public static string PL(string msgid) => Pinta.Core.Translations.GetString(msgid);
}
