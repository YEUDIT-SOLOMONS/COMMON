namespace PhotoRental.Data;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Google.Apis.Auth.OAuth2;

// עטיפה לחיבור ל-Firestore, המשמשת כמקור נתונים יחיד (Single Source) לכל שכבת ה-Data.
//
// מימוש זה פונה ל-Firestore REST API (גרסה v1) ישירות דרך HttpClient רגיל, במקום להשתמש
// בחבילת ה-SDK הרשמית (Google.Cloud.Firestore) המבוססת על gRPC (הדורש חיבור HTTP/2).
// הסיבה למעבר: בסביבות מסוימות (למשל תוכנת אנטי-וירוס/פרוקסי מקומי החוסם משא ומתן HTTP/2)
// לא ניתן לפתוח חיבור gRPC ל-Firestore בכלל, בעוד שבקשות REST רגילות מעל HTTP/1.1 עובדות תקין.
// לכן כל בקשה כאן נכפית באופן מפורש ל-HTTP/1.1, כדי לעקוף את הבעיה הזו לחלוטין.
public class FirebaseDbContext
{
    private static readonly string[] FirestoreScopes = { "https://www.googleapis.com/auth/datastore" };

    private readonly HttpClient _http;
    private readonly GoogleCredential _credential;
    private readonly string _documentsBasePath;

    public string ProjectId { get; }

    public FirebaseDbContext(string projectId, string jsonCredentialsPath)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException(
                "יש להגדיר את מזהה הפרויקט (Firebase:ProjectId) בקובץ ההגדרות appsettings.json",
                nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(jsonCredentialsPath))
        {
            throw new ArgumentException(
                "יש להגדיר את נתיב קובץ ההרשאות (Firebase:CredentialsPath) בקובץ ההגדרות appsettings.json",
                nameof(jsonCredentialsPath));
        }

        if (!File.Exists(jsonCredentialsPath))
        {
            throw new FileNotFoundException(
                $"קובץ ההרשאות של Firebase לא נמצא בנתיב: {jsonCredentialsPath}. " +
                "יש לוודא שהקובץ (firebase-key.json) קיים ושה-Firebase:CredentialsPath ב-appsettings.json מצביע אליו נכון.",
                jsonCredentialsPath);
        }

        ProjectId = projectId;

        // קריאת קובץ המפתח מהדיסק בלבד (ללא כל קריאת רשת) - בניית האובייקט כאן חייבת
        // להישאר קלה ומהירה, כדי שלא תפיל את עליית האפליקציה. בעיות רשת/הרשאות אמיתיות
        // יתגלו רק בפועל בעת קריאת ה-API הראשונה שמנסה לגשת ל-Firestore, ויוצגו כתשובת
        // שגיאה מפורטת (דרך ה-Middleware הגלובלי) במקום להקריס את השרת כולו.
        try
        {
            // GoogleCredential.FromFile מסומנת כמיושנת (Obsolete) ומציעה כחלופה את CredentialFactory,
            // אך זו דורשת לדעת מראש את הסוג הקונקרטי של קובץ המפתח (למשל ServiceAccountCredential),
            // ובניסיון קודם עם CredentialFactory.FromFile<GoogleCredential> נכשלה בפועל בזמן ריצה
            // (החריגה: "The credential type ServiceAccountCredential is not compatible with the requested
            // type GoogleCredential"). GoogleCredential.FromFile לעומת זאת מזהה את סוג הקובץ אוטומטית
            // ותמיד מחזירה GoogleCredential תקין - לכן חוזרים לשימוש בה, ומשתיקים כאן במפורש רק
            // את אזהרת ה-Obsolete הזו (ולא כל אזהרה אחרת בקובץ).
#pragma warning disable CS0618
            _credential = GoogleCredential.FromFile(jsonCredentialsPath).CreateScoped(FirestoreScopes);
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"קריאת קובץ ההרשאות של Firebase נכשלה ({jsonCredentialsPath}). ייתכן שהקובץ פגום או אינו קובץ JSON " +
                $"תקין של חשבון שירות (Service Account). פרטי השגיאה: {ex.Message}",
                ex);
        }

        _documentsBasePath = $"projects/{projectId}/databases/(default)/documents";

        _http = new HttpClient
        {
            BaseAddress = new Uri("https://firestore.googleapis.com/v1/")
        };
    }

    // ===================== פעולות בסיסיות על מסמכים =====================

    // הגדלה אטומית של שדות מספריים (יוצר את המסמך אם אינו קיים) - נכון גם כשכמה בקשות מגיעות בבת אחת
    public async Task IncrementAsync(string collectionId, string documentId, IReadOnlyDictionary<string, long> increments)
    {
        var transforms = new JsonArray();
        foreach (var (field, by) in increments)
            transforms.Add(new JsonObject { ["fieldPath"] = field, ["increment"] = new JsonObject { ["integerValue"] = by.ToString() } });

        var body = new JsonObject
        {
            ["writes"] = new JsonArray
            {
                new JsonObject
                {
                    ["transform"] = new JsonObject
                    {
                        ["document"] = $"{_documentsBasePath}/{collectionId}/{documentId}",
                        ["fieldTransforms"] = transforms
                    }
                }
            }
        };
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:commit", body);
        await EnsureSuccessAsync(response);
    }

    // שליפת מסמך בודד לפי מזהה. מחזיר null אם המסמך לא קיים (ולא זורק חריגה) -
    // כדי לאפשר לשכבות שמעל להבחין בקלות בין "לא נמצא" לבין שגיאה אמיתית.
    public async Task<JsonObject?> GetDocumentAsync(string collectionId, string documentId)
    {
        var response = await SendAsync(HttpMethod.Get, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response);
        return await ReadJsonObjectAsync(response);
    }

    // שליפת כל המסמכים באוסף, עם דפדוף (Pagination) אוטומטי ומלא כדי לא להחזיר רק עמוד חלקי
    public async Task<IReadOnlyList<JsonObject>> ListDocumentsAsync(string collectionId)
    {
        var results = new List<JsonObject>();
        string? pageToken = null;

        do
        {
            var url = $"{_documentsBasePath}/{collectionId}?pageSize=300";
            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            var response = await SendAsync(HttpMethod.Get, url);
            await EnsureSuccessAsync(response);
            var body = await ReadJsonObjectAsync(response);

            if (body?["documents"] is JsonArray documents)
            {
                foreach (var doc in documents)
                {
                    if (doc is JsonObject docObj)
                    {
                        results.Add(docObj);
                    }
                }
            }

            pageToken = body?["nextPageToken"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    // יצירת מסמך חדש באוסף, עם מזהה שנוצר אוטומטית ע"י Firestore
    private static void InvalidateCatalogFor(string collectionId)
    {
        if (collectionId is "Items" or "Categories" or "Loans" or "CommissionStatements")
            PhotoRental.Core.Helpers.CatalogCache.Invalidate();
    }

    public async Task<JsonObject> CreateDocumentAsync(string collectionId, JsonObject fields)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}/{collectionId}", body);
        await EnsureSuccessAsync(response);
        var created = await ReadJsonObjectAsync(response);

        return created ?? throw new InvalidOperationException(
            $"Firestore החזיר תשובה ריקה לאחר יצירת מסמך באוסף '{collectionId}'.");
    }

    // החלפת כל השדות של מסמך קיים (ללא updateMask - Firestore מחליף את כל תוכן המסמך בשדות שנשלחו)
    public async Task ReplaceDocumentAsync(string collectionId, string documentId, JsonObject fields)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var response = await SendAsync(HttpMethod.Patch, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}", body);
        await EnsureSuccessAsync(response);
    }

    // עדכון חלקי של שדות ספציפיים בלבד במסמך קיים (updateMask), מבלי לגעת בשאר השדות שבמסמך
    public async Task PatchDocumentFieldsAsync(string collectionId, string documentId, JsonObject fields, IEnumerable<string> fieldPaths)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var maskQuery = string.Join("&", fieldPaths.Select(path => $"updateMask.fieldPaths={Uri.EscapeDataString(path)}"));
        var url = $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}?{maskQuery}";

        var response = await SendAsync(HttpMethod.Patch, url, body);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteDocumentAsync(string collectionId, string documentId)
    {
        InvalidateCatalogFor(collectionId);
        var response = await SendAsync(HttpMethod.Delete, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response);
    }

    // הרצת שאילתה מובנית (Structured Query) מול אוסף, עם עד כמה תנאי שוויון (AND ביניהם) ומגבלת תוצאות אופציונלית
    public async Task<IReadOnlyList<JsonObject>> RunQueryAsync(
        string collectionId,
        IReadOnlyList<(string FieldPath, JsonNode Value)> equalityFilters,
        int? limit = null)
    {
        JsonNode? where = null;

        if (equalityFilters.Count == 1)
        {
            where = BuildFieldEqualsFilter(equalityFilters[0].FieldPath, equalityFilters[0].Value);
        }
        else if (equalityFilters.Count > 1)
        {
            var conditions = new JsonArray();
            foreach (var (fieldPath, value) in equalityFilters)
            {
                conditions.Add(BuildFieldEqualsFilter(fieldPath, value));
            }

            where = new JsonObject
            {
                ["compositeFilter"] = new JsonObject
                {
                    ["op"] = "AND",
                    ["filters"] = conditions
                }
            };
        }

        var structuredQuery = new JsonObject
        {
            ["from"] = new JsonArray { new JsonObject { ["collectionId"] = collectionId } }
        };

        if (where is not null)
        {
            structuredQuery["where"] = where;
        }

        if (limit.HasValue)
        {
            structuredQuery["limit"] = limit.Value;
        }

        var body = new JsonObject { ["structuredQuery"] = structuredQuery };
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:runQuery", body);
        await EnsureSuccessAsync(response);

        var resultsArray = await ReadJsonArrayAsync(response);
        var documents = new List<JsonObject>();

        if (resultsArray is not null)
        {
            foreach (var entry in resultsArray)
            {
                if (entry is JsonObject entryObj && entryObj["document"] is JsonObject docObj)
                {
                    documents.Add(docObj);
                }
            }
        }

        return documents;
    }

    private static JsonObject BuildFieldEqualsFilter(string fieldPath, JsonNode value)
    {
        return new JsonObject
        {
            ["fieldFilter"] = new JsonObject
            {
                ["field"] = new JsonObject { ["fieldPath"] = fieldPath },
                ["op"] = "EQUAL",
                ["value"] = value
            }
        };
    }

    // ===================== תשתית פנימית לשליחת בקשות HTTP =====================

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, JsonNode? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            // כפיית HTTP/1.1 באופן מפורש לכל בקשה - זו הסיבה המרכזית למעבר מ-gRPC ל-REST:
            // עוקפת סביבות (אנטי-וירוס/פרוקסי מקומי) שחוסמות משא ומתן (Negotiation) של HTTP/2.
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        string accessToken;
        try
        {
            // GoogleCredential מממש את ITokenAccess באופן מפורש (Explicit Interface Implementation),
            // ולכן חובה להמיר (Cast) לממשק כדי לקרוא ל-GetAccessTokenForRequestAsync - קריאה ישירה
            // דרך המשתנה מסוג GoogleCredential אינה מתקמפלת (שגיאת CS1061).
            accessToken = await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "האימות מול Google נכשל בעת ניסיון לקבל טוקן גישה. ייתכן שקובץ המפתח (firebase-key.json) פג תוקף, " +
                $"נפגם, בוטל, או שאין חיבור רשת לשרתי האימות של Google. פרטי השגיאה: {ex.Message}",
                ex);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody.ToJsonString(), Encoding.UTF8, "application/json");
        }

        try
        {
            return await _http.SendAsync(request);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "החיבור לשרתי Firestore (firestore.googleapis.com) נכשל. יש לבדוק חיבור אינטרנט, " +
                $"ושחומת אש/אנטי-וירוס אינם חוסמים את הגישה. פרטי השגיאה המקוריים: {ex.Message}",
                ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var bodyText = await response.Content.ReadAsStringAsync();
        var googleMessage = TryExtractGoogleErrorMessage(bodyText) ?? bodyText;

        var explanation = response.StatusCode switch
        {
            HttpStatusCode.Forbidden =>
                $"לחשבון השירות (בקובץ המפתח firebase-key.json) אין הרשאה לגשת ל-Firestore בפרויקט '{ProjectId}'. " +
                "יש להיכנס ל-Google Cloud Console -> IAM & Admin, ולהוסיף לחשבון השירות (client_email מתוך קובץ המפתח) " +
                "את התפקיד 'Cloud Datastore User' (roles/datastore.user).",
            HttpStatusCode.Unauthorized =>
                "האימות מול Google נכשל. ייתכן שקובץ המפתח (firebase-key.json) פג תוקף, נפגם, בוטל, " +
                "או שייך לפרויקט אחר מזה שמוגדר ב-Firebase:ProjectId.",
            HttpStatusCode.NotFound =>
                $"לא נמצא מאגר Firestore בפרויקט '{ProjectId}', או שהנתיב המבוקש אינו קיים. " +
                "יש לוודא בקונסולת Firebase שנוצר מסד נתונים מסוג Cloud Firestore (במצב Native) עבור הפרויקט הזה בדיוק.",
            HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout =>
                "שרתי Google Cloud (firestore.googleapis.com) אינם זמינים כרגע. יש לנסות שוב בעוד רגע.",
            _ => (string?)null
        };

        var message = $"הבקשה ל-Firestore נכשלה (קוד סטטוס HTTP: {(int)response.StatusCode} {response.StatusCode})." +
            (explanation is not null ? $" {explanation}" : string.Empty) +
            $" פרטי השגיאה מגוגל: {googleMessage}";

        throw new InvalidOperationException(message);
    }

    private static string? TryExtractGoogleErrorMessage(string bodyText)
    {
        try
        {
            var node = JsonNode.Parse(bodyText);

            if (node is JsonArray array && array.Count > 0)
            {
                node = array[0];
            }

            return node?["error"]?["message"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<JsonObject?> ReadJsonObjectAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text) as JsonObject;
    }

    private static async Task<JsonArray?> ReadJsonArrayAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonNode.Parse(text) as JsonArray;
    }
}
