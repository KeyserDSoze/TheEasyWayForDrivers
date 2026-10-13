namespace TheEasyWayForDrivers.Core.Security;

/// <summary>
/// Composes independent trust evidence; deliberately never authorizes OEM installs.
/// CAT signature and per-file membership must never be conflated.
/// </summary>
public static class OemPackageTrustAssessment
{
    public enum Risk { Rejected, IncompleteEvidence, PartialEvidence }
    public sealed record Report(Risk Result, int VerifiedFiles, int RejectedFiles,
        int UnverifiedFiles, bool CatalogSignatureValid, string Explanation)
    {
        public bool IsInstallable => false;
        public bool IsPackageVerified => false;
    }

    public static Report Assess(CatalogTrustReport.Result? catalogSignature,
        DriverPayloadAuditPolicy.Summary? payload,
        InfDependencyPreview.Report? dependencies)
    {
        var validCatalog = catalogSignature?.SignatureStatus ==
            CatalogTrustReport.Status.ValidCatalogSignature;
        var rejectedSignature = catalogSignature?.SignatureStatus ==
            CatalogTrustReport.Status.InvalidSignature;
        var verified = payload?.Verified ?? 0;
        var rejected = payload?.Rejected ?? 0;
        var unknown = payload?.NotVerified ?? 0;
        var risk = rejectedSignature || rejected > 0
            ? Risk.Rejected
            : validCatalog && verified > 0 && unknown == 0 &&
                payload?.OtherFiles == 0 &&
                dependencies is { MissingCount: 0, UnresolvedCount: 0 }
                && dependencies.Warnings.Count == 0
                    ? Risk.PartialEvidence : Risk.IncompleteEvidence;
        var explanation = risk switch
        {
            Risk.Rejected => "CAT o file respinto: non usare il pacchetto.",
            Risk.PartialEvidence =>
                "Alcuni file sono stati verificati, ma completezza, policy kernel, revoca e applicabilità Windows NON sono provate.",
            _ => "Verifiche CAT, membership o dipendenze mancanti/incomplete. Nessuna fiducia nel pacchetto."
        };
        return new Report(risk, verified, rejected, unknown, validCatalog, explanation);
    }
}
