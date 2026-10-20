namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// מצב הגבייה של דוחות העמלה החודשיים (אוסף CommissionStatements ב-Firestore): אחד לכל משכירה לכל חודש
public class CommissionStatementRepository : ICommissionStatementRepository
{
    private const string CollectionName = "CommissionStatements";
    private readonly FirebaseDbContext _firebase;

    public CommissionStatementRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<CommissionStatement>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<CommissionStatement> UpsertAsync(CommissionStatement statement)
    {
        if (string.IsNullOrEmpty(statement.Id))
        {
            var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(statement));
            statement.Id = FirestoreJson.ExtractDocumentId(created);
        }
        else
        {
            await _firebase.ReplaceDocumentAsync(CollectionName, statement.Id, ToFields(statement));
        }

        return statement;
    }

    private static JsonObject ToFields(CommissionStatement statement)
    {
        return new JsonObject
        {
            [nameof(CommissionStatement.OwnerId)] = FirestoreJson.StringField(statement.OwnerId),
            [nameof(CommissionStatement.Month)] = FirestoreJson.StringField(statement.Month),
            [nameof(CommissionStatement.Paid)] = FirestoreJson.BooleanField(statement.Paid),
            [nameof(CommissionStatement.PaidAtUtc)] = FirestoreJson.TimestampOrNullField(statement.PaidAtUtc),
            [nameof(CommissionStatement.SentAtUtc)] = FirestoreJson.TimestampOrNullField(statement.SentAtUtc),
            [nameof(CommissionStatement.ProofToken)] = FirestoreJson.StringOrNullField(statement.ProofToken),
            [nameof(CommissionStatement.ProofId)] = FirestoreJson.StringOrNullField(statement.ProofId),
            [nameof(CommissionStatement.ProofUploadedAtUtc)] = FirestoreJson.TimestampOrNullField(statement.ProofUploadedAtUtc),
            [nameof(CommissionStatement.AutoChargeAttemptedAtUtc)] = FirestoreJson.TimestampOrNullField(statement.AutoChargeAttemptedAtUtc),
            [nameof(CommissionStatement.DebtWarningSentAtUtc)] = FirestoreJson.TimestampOrNullField(statement.DebtWarningSentAtUtc),
            [nameof(CommissionStatement.DebtBlockedNoticeSentAtUtc)] = FirestoreJson.TimestampOrNullField(statement.DebtBlockedNoticeSentAtUtc),
            [nameof(CommissionStatement.OffsetAmount)] = FirestoreJson.DoubleField((double)statement.OffsetAmount),
            [nameof(CommissionStatement.OffsetSettledAtUtc)] = FirestoreJson.TimestampOrNullField(statement.OffsetSettledAtUtc)
        };
    }

    private static CommissionStatement ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new CommissionStatement
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            OwnerId = FirestoreJson.GetString(fields, nameof(CommissionStatement.OwnerId)),
            Month = FirestoreJson.GetString(fields, nameof(CommissionStatement.Month)),
            Paid = FirestoreJson.GetBool(fields, nameof(CommissionStatement.Paid)),
            PaidAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.PaidAtUtc)),
            SentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.SentAtUtc)),
            ProofToken = FirestoreJson.GetStringOrNull(fields, nameof(CommissionStatement.ProofToken)),
            ProofId = FirestoreJson.GetStringOrNull(fields, nameof(CommissionStatement.ProofId)),
            ProofUploadedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.ProofUploadedAtUtc)),
            AutoChargeAttemptedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.AutoChargeAttemptedAtUtc)),
            DebtWarningSentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.DebtWarningSentAtUtc)),
            DebtBlockedNoticeSentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.DebtBlockedNoticeSentAtUtc)),
            OffsetAmount = (decimal)FirestoreJson.GetDouble(fields, nameof(CommissionStatement.OffsetAmount)),
            OffsetSettledAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CommissionStatement.OffsetSettledAtUtc))
        };
    }
}
