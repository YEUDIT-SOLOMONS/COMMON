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

    // ה-CancellationToken של הבקשה הנוכחית (HttpContext.RequestAborted). קריאה שלא קיבלה token מפורש משתמשת בו,
    // כך שביטול בקשה עוצר גם את הפנייה ל-Firestore בכל ה-endpoints
    public Func<CancellationToken>? AmbientCancellation { get; set; }
    private readonly GoogleCredential? _credential;
    private readonly string _documentsBasePath;

    public string ProjectId { get; }

    public bool IsEmulator => _credential is null;

    // חיבור ל-Firestore Emulator מקומי (לפיתוח ולבדיקות) - בלי קובץ מפתח ובלי גישה לענן
    public FirebaseDbContext(string projectId, Uri emulatorBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("יש להגדיר את Firebase:ProjectId", nameof(projectId));

        ProjectId = projectId;
        _credential = null;
        _documentsBasePath = $"projects/{projectId}/databases/(default)/documents";
        _http = new HttpClient { BaseAddress = new Uri(emulatorBaseUrl, "v1/") };
    }

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
    public async Task IncrementAsync(string collectionId, string documentId, IReadOnlyDictionary<string, long> increments, CancellationToken ct = default)
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
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:commit", body, ct: ct);
        await EnsureSuccessAsync(response, ct);
    }

    // שליפת מסמך בודד לפי מזהה. מחזיר null אם המסמך לא קיים (ולא זורק חריגה) -
    // כדי לאפשר לשכבות שמעל להבחין בקלות בין "לא נמצא" לבין שגיאה אמיתית.
    public async Task<JsonObject?> GetDocumentAsync(string collectionId, string documentId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}", ct: ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, ct);
        return await ReadJsonObjectAsync(response);
    }

    // שליפת כל המסמכים באוסף, עם דפדוף (Pagination) אוטומטי ומלא כדי לא להחזיר רק עמוד חלקי
    public async Task<IReadOnlyList<JsonObject>> ListDocumentsAsync(string collectionId, CancellationToken ct = default)
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

            var response = await SendAsync(HttpMethod.Get, url, ct: ct);
            await EnsureSuccessAsync(response, ct);
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

    public async Task<JsonObject> CreateDocumentAsync(string collectionId, JsonObject fields, CancellationToken ct = default)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}/{collectionId}", body, ct: ct);
        await EnsureSuccessAsync(response, ct);
        var created = await ReadJsonObjectAsync(response);

        return created ?? throw new InvalidOperationException(
            $"Firestore החזיר תשובה ריקה לאחר יצירת מסמך באוסף '{collectionId}'.");
    }

    // החלפת כל השדות של מסמך קיים (ללא updateMask - Firestore מחליף את כל תוכן המסמך בשדות שנשלחו)
    public async Task ReplaceDocumentAsync(string collectionId, string documentId, JsonObject fields, CancellationToken ct = default)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var response = await SendAsync(HttpMethod.Patch, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}", body, ct: ct);
        await EnsureSuccessAsync(response, ct);
    }

    // עדכון חלקי של שדות ספציפיים בלבד במסמך קיים (updateMask), מבלי לגעת בשאר השדות שבמסמך
    public async Task PatchDocumentFieldsAsync(string collectionId, string documentId, JsonObject fields, IEnumerable<string> fieldPaths, CancellationToken ct = default)
    {
        InvalidateCatalogFor(collectionId);
        var body = new JsonObject { ["fields"] = fields };
        var maskQuery = string.Join("&", fieldPaths.Select(path => $"updateMask.fieldPaths={Uri.EscapeDataString(path)}"));
        var url = $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}?{maskQuery}";

        var response = await SendAsync(HttpMethod.Patch, url, body, ct: ct);
        await EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteDocumentAsync(string collectionId, string documentId, CancellationToken ct = default)
    {
        InvalidateCatalogFor(collectionId);
        var response = await SendAsync(HttpMethod.Delete, $"{_documentsBasePath}/{collectionId}/{Uri.EscapeDataString(documentId)}", ct: ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, ct);
    }

    // הרצת שאילתה מובנית (Structured Query) מול אוסף, עם עד כמה תנאי שוויון (AND ביניהם) ומגבלת תוצאות אופציונלית
    public async Task<IReadOnlyList<JsonObject>> RunQueryAsync(
        string collectionId,
        IReadOnlyList<(string FieldPath, JsonNode Value)> equalityFilters,
        int? limit = null,
        CancellationToken ct = default)
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
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:runQuery", body, ct: ct);
        await EnsureSuccessAsync(response, ct);

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

    // שליפת כמה מסמכים לפי מזהה בבקשה אחת (המקבילה של Include) - במקום בקשה לכל מסמך בלולאה (N+1)
    public async Task<IReadOnlyList<JsonObject>> BatchGetAsync(string collectionId, IEnumerable<string> documentIds, CancellationToken ct = default)
    {
        var names = documentIds.Where(id => !string.IsNullOrEmpty(id)).Distinct()
            .Select(id => (JsonNode)DocumentName(collectionId, id)).ToArray();
        if (names.Length == 0)
            return Array.Empty<JsonObject>();

        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:batchGet", new JsonObject { ["documents"] = new JsonArray(names) }, ct: ct);
        await EnsureSuccessAsync(response, ct);

        var results = await ReadJsonArrayAsync(response);
        return results?.OfType<JsonObject>().Select(r => r["found"]).OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    }

    // ===================== שאילתות עם מיון ודפדוף בצד השרת =====================

    public static JsonObject EqualsFilter(string fieldPath, JsonNode value) => BuildFieldEqualsFilter(fieldPath, value);

    public static JsonObject IsNullFilter(string fieldPath) => new()
    {
        ["unaryFilter"] = new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = fieldPath }, ["op"] = "IS_NULL" }
    };

    private static JsonObject? CombineFilters(IReadOnlyList<JsonObject> filters) => filters.Count switch
    {
        0 => null,
        1 => (JsonObject)filters[0].DeepClone(),
        _ => new JsonObject
        {
            ["compositeFilter"] = new JsonObject { ["op"] = "AND", ["filters"] = new JsonArray(filters.Select(f => f.DeepClone()).ToArray()) }
        }
    };

    // דפדוף אמיתי: offset ו-limit נשלחים בתוך השאילתה (המקבילה של Skip ו-Take), לא חיתוך בזיכרון
    public async Task<IReadOnlyList<JsonObject>> QueryPageAsync(
        string collectionId,
        IReadOnlyList<JsonObject> filters,
        IReadOnlyList<(string FieldPath, bool Descending)> orderBy,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        var query = new JsonObject
        {
            ["from"] = new JsonArray { new JsonObject { ["collectionId"] = collectionId } },
            ["orderBy"] = new JsonArray(orderBy.Select(o => (JsonNode)new JsonObject
            {
                ["field"] = new JsonObject { ["fieldPath"] = o.FieldPath },
                ["direction"] = o.Descending ? "DESCENDING" : "ASCENDING"
            }).ToArray()),
            ["offset"] = offset,
            ["limit"] = limit
        };
        if (CombineFilters(filters) is { } where)
            query["where"] = where;

        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:runQuery", new JsonObject { ["structuredQuery"] = query }, ct: ct);
        await EnsureSuccessAsync(response, ct);

        var results = await ReadJsonArrayAsync(response);
        return results?.OfType<JsonObject>().Select(r => r["document"]).OfType<JsonObject>().ToList() ?? new List<JsonObject>();
    }

    // ספירה בצד השרת (aggregation query) - בלי להוריד את המסמכים עצמם
    public async Task<int> CountAsync(string collectionId, IReadOnlyList<JsonObject> filters, CancellationToken ct = default)
    {
        var query = new JsonObject { ["from"] = new JsonArray { new JsonObject { ["collectionId"] = collectionId } } };
        if (CombineFilters(filters) is { } where)
            query["where"] = where;

        var body = new JsonObject
        {
            ["structuredAggregationQuery"] = new JsonObject
            {
                ["structuredQuery"] = query,
                ["aggregations"] = new JsonArray { new JsonObject { ["alias"] = "total", ["count"] = new JsonObject() } }
            }
        };

        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:runAggregationQuery", body, ct: ct);
        await EnsureSuccessAsync(response, ct);

        var raw = (await ReadJsonArrayAsync(response))?.OfType<JsonObject>()
            .Select(r => r["result"]?["aggregateFields"]?["total"]?["integerValue"]?.GetValue<string>())
            .FirstOrDefault(v => v is not null);
        return int.TryParse(raw, out var count) ? count : 0;
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

    // ===================== שמירה אטומית עם concurrency token =====================

    public string DocumentName(string collectionId, string documentId) => $"{_documentsBasePath}/{collectionId}/{documentId}";

    public static string NewDocumentId() => Guid.NewGuid().ToString("N")[..20];

    // כתיבה למסמך חדש - נכשלת אם כבר קיים מסמך עם אותו מזהה
    public JsonObject CreateWrite(string collectionId, string documentId, JsonObject fields) => new()
    {
        ["update"] = new JsonObject { ["name"] = DocumentName(collectionId, documentId), ["fields"] = fields },
        ["currentDocument"] = new JsonObject { ["exists"] = false }
    };

    // עדכון שדות שמתבצע רק אם המסמך לא השתנה מאז שנקרא (updateTime זהה) - כמו תנאי ה-WHERE של EF Core
    public JsonObject GuardedPatchWrite(string collectionId, string documentId, JsonObject fields, string expectedUpdateTime) => new()
    {
        ["update"] = new JsonObject { ["name"] = DocumentName(collectionId, documentId), ["fields"] = fields },
        ["updateMask"] = new JsonObject { ["fieldPaths"] = new JsonArray(fields.Select(f => (JsonNode)f.Key).ToArray()) },
        ["currentDocument"] = new JsonObject { ["updateTime"] = expectedUpdateTime }
    };

    // כל הכתיבות נשמרות יחד או שאף אחת לא נשמרת. אם תנאי מוקדם נכשל - ConcurrencyConflictException
    public async Task CommitAsync(IEnumerable<JsonObject> writes, string? resourceId = null, CancellationToken ct = default)
    {
        InvalidateCatalogFor("Items");
        var body = new JsonObject { ["writes"] = new JsonArray(writes.Cast<JsonNode>().ToArray()) };
        var response = await SendAsync(HttpMethod.Post, $"{_documentsBasePath}:commit", body, ct: ct);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Contains("FAILED_PRECONDITION") || text.Contains("ABORTED") || text.Contains("ALREADY_EXISTS"))
                throw new PhotoRental.Core.Exceptions.ConcurrencyConflictException(
                    "המשאב השתנה על ידי משתמש אחר בין הקריאה לשמירה", resourceId);
            throw new PhotoRental.Core.Exceptions.DataAccessException($"שמירה ל-Firestore נכשלה: {TryExtractGoogleErrorMessage(text) ?? text}");
        }

        await EnsureSuccessAsync(response, ct);
    }

    // ===================== תשתית פנימית לשליחת בקשות HTTP =====================

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string relativeUrl, JsonNode? jsonBody = null, CancellationToken ct = default)
    {
        if (!ct.CanBeCanceled && AmbientCancellation is not null)
            ct = AmbientCancellation();
        var request = new HttpRequestMessage(method, relativeUrl)
        {
            // כפיית HTTP/1.1 באופן מפורש לכל בקשה - זו הסיבה המרכזית למעבר מ-gRPC ל-REST:
            // עוקפת סביבות (אנטי-וירוס/פרוקסי מקומי) שחוסמות משא ומתן (Negotiation) של HTTP/2.
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };

        string accessToken = "owner";
        if (_credential is not null)
        try
        {
            // GoogleCredential מממש את ITokenAccess באופן מפורש (Explicit Interface Implementation),
            // ולכן חובה להמיר (Cast) לממשק כדי לקרוא ל-GetAccessTokenForRequestAsync - קריאה ישירה
            // דרך המשתנה מסוג GoogleCredential אינה מתקמפלת (שגיאת CS1061).
            accessToken = await ((ITokenAccess)_credential).GetAccessTokenForRequestAsync();
        }
        catch (Exception ex)
        {
            throw new PhotoRental.Core.Exceptions.DataAccessException(
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
            return await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw new PhotoRental.Core.Exceptions.DataAccessException(
                "החיבור לשרתי Firestore (firestore.googleapis.com) נכשל. יש לבדוק חיבור אינטרנט, " +
                $"ושחומת אש/אנטי-וירוס אינם חוסמים את הגישה. פרטי השגיאה המקוריים: {ex.Message}",
                ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var bodyText = await response.Content.ReadAsStringAsync(ct);
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

        throw new PhotoRental.Core.Exceptions.DataAccessException(message);
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
