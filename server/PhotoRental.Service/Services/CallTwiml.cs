namespace PhotoRental.Service.Services;

using System.Security;

// בניית TwiML (הוראות השיחה ל-Twilio) לשיחות האוטומטיות
public static class CallTwiml
{
    // הקול של הקריין. הקולות הרגילים של Twilio אינם תומכים בעברית (הם רק מקריאים ספרות) - לכן חייבים קול של גוגל בעברית:
    // Google.he-IL-Wavenet-A / C (נשי), B / D (גברי); גרסאות Standard זולות יותר ופחות טבעיות. ניתן לשנות ב-Twilio:Voice.
    public static string Voice { get; set; } = "Google.he-IL-Wavenet-A";

    // תג <Say> עם הקול והשפה, כשהטקסט כבר מוכן ל-XML (escaped)
    public static string SayTag(string escapedText) => $"<Say voice=\"{Voice}\" language=\"he-IL\">{escapedText}</Say>";

    private const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response>";

    private static string Say(string text) => SayTag(SecurityElement.Escape(text));

    // הודעה חד-כיוונית (פעם אחת או פעמיים)
    public static string OneWay(string message, bool repeat)
    {
        var body = "<Pause length=\"1\"/>" + Say(message) +
                   (repeat ? "<Pause length=\"2\"/>" + Say(message) : string.Empty);
        return Header + body + "</Response>";
    }

    // הודעה כתפריט: המאזינה מקישה ספרה, והיא נשלחת ב-POST לכתובת actionUrl. בלי הקשה - נאמר "תודה" והשיחה מסתיימת.
    public static string Gather(string message, string actionUrl) =>
        Header +
        "<Pause length=\"1\"/>" +
        $"<Gather numDigits=\"1\" timeout=\"15\" action=\"{SecurityElement.Escape(actionUrl)}\" method=\"POST\">" + Say(message) + "</Gather>" +
        Say("לא התקבלה תגובה. להתראות.") +
        "</Response>";

    public static string Bridge(string intro, string numberE164, string callerId, string doneUrl, string? whisperUrl = null) =>
        Header + Say(intro) +
        $"<Dial callerId=\"{SecurityElement.Escape(callerId)}\" timeout=\"25\" timeLimit=\"600\" answerOnBridge=\"true\" action=\"{SecurityElement.Escape(doneUrl)}\" method=\"POST\">" +
        (whisperUrl is null ? "<Number>" : $"<Number url=\"{SecurityElement.Escape(whisperUrl)}\" method=\"POST\">") +
        $"{SecurityElement.Escape(numberE164)}</Number></Dial></Response>";

    // הודעה שמושמעת רק לצד שמקבל את השיחה, לפני החיבור
    public static string Whisper(string message) => Header + Say(message) + "</Response>";

    // מוסיף הקראה לפני שאר הוראות השיחה (למשל השמעת הודעה שלא נשמעה, ואז התפריט הרגיל)
    public static string Prepend(string message, string twiml) =>
        twiml.StartsWith(Header, StringComparison.Ordinal) ? Header + Say(message) + "<Pause length=\"1\"/>" + twiml[Header.Length..] : twiml;

    public static string Empty() => Header + "<Hangup/></Response>";

    // הודעה אחרונה ואז ניתוק
    public static string SayAndHangup(string message) => Header + Say(message) + "<Hangup/></Response>";
}
