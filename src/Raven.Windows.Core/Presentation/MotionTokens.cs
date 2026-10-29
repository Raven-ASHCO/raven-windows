namespace Raven.Windows.Core.Presentation;

/// <summary>
/// tokens.json "motion.durationsMs" (ThemeTokensTests checks the values). Honest motion:
/// each animation is started only by a real engine event (DESIGN.md, "Honest motion");
/// the logo reveal on the welcome screen is the only decorative one.
/// </summary>
public static class MotionTokens
{
    public const int BubbleIn = 350;
    public const int RoutePacket = 700;
    public const int ReceiptBack = 600;
    public const int HeartbeatPulse = 500;
    public const int StatusCheckInterval = 2000;
    public const int PresenceBreathe = 3000;
    public const int RetryRing = 1000;
    public const int LogoReveal = 600;
    public const int WordReveal = 200;
    public const int ReadAloudStep = 800;
    public const int ToastVisible = 5000;

    /// <summary>Incoming bubbles stay bold this long (DESIGN.md "Incoming").</summary>
    public const int IncomingBold = 1500;

    /// <summary>Events older than this when they reach the app update text without moving anything.</summary>
    public const int StaleEventMs = 3000;

    /// <summary>API.md: poll every 500 ms while a window is open and every 5 s in the tray.</summary>
    public const int PollWindowMs = 500;
    public const int PollTrayMs = 5000;
}
