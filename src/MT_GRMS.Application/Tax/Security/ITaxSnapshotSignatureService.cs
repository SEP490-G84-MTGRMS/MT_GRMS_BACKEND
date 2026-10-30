using MT_GRMS.Domain.Entities.Finance;

namespace MT_GRMS.Application.Tax.Security;

/// <summary>
/// Service contract for HMAC-SHA256 signature generation and integrity verification for household tax snapshots.
/// </summary>
public interface ITaxSnapshotSignatureService
{
    /// <summary>
    /// Computes HMAC-SHA256 signature from canonical JSON of tax snapshot.
    /// </summary>
    string ComputeSignature(HouseholdTaxSnapshot snapshot);

    /// <summary>
    /// Verifies if the snapshot data has been altered or tampered with.
    /// </summary>
    bool VerifySignature(HouseholdTaxSnapshot snapshot);
}
