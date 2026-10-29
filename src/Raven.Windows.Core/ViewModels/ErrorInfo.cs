using System;
using Raven.Windows.Core.Engine;
using Raven.Windows.Core.Text;

namespace Raven.Windows.Core.ViewModels;

/// <summary>
/// An error as the screens show it: the engine's plain message (what happened and one next
/// step) with the technical text under Details (DESIGN.md, "One next step").
/// </summary>
public sealed record ErrorInfo(string Code, string Message, string Detail)
{
    public static ErrorInfo From(Exception ex) => ex is RavenException raven
        ? new ErrorInfo(raven.Code, raven.Message, raven.Detail)
        : new ErrorInfo(RavenErrorCodes.Internal, Loc.Get("error.internal"), ex.GetType().Name + ": " + TextSanitizer.Clean(ex.Message));

    /// <summary>NOT_AVAILABLE_YET: held in this build; nothing was changed.</summary>
    public bool IsNotAvailableYet => Code == RavenErrorCodes.NotAvailableYet;

    /// <summary>CONSENT_NEEDED: the setting needs the person's yes first.</summary>
    public bool IsConsentNeeded => Code == RavenErrorCodes.ConsentNeeded;
}
