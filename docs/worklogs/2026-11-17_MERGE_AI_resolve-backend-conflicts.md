# Backend merge conflict resolution — 2026-11-17

Resolved 17 conflicted files in the pending merge on `feature/audit-fe7-fe19-finance-tax-verified`.

Combined incoming POS, SePay, dashboard and payment-account changes with tenant/branch authorization, DataProtection, security stamps, hashed rotating refresh tokens and finance/tax controls. Preserved employee deactivation checks during JWT validation. Updated authentication and POS test fixtures to exercise server-trusted roles and issued cookies.

Validation:
- `dotnet build MT_GRMS_BACKEND.sln --no-restore -m:1`: passed, compiler warnings remain.
- `dotnet test MT_GRMS_BACKEND.sln --no-restore -m:1`: 1188 unit tests and 104 integration tests passed; 4 PostgreSQL-dependent tests skipped.
- `git diff --check`: passed.

No push performed.
