# Resolve the wallet branch merge

Resolved the conflict in the pending merge of `dev` (`c51caa46`) into `refactor/wallet-management`. The only unmerged file was `src/MT_GRMS.Infrastructure/Persistence/DbInitializer.cs`.

Kept the scenario seeder introduced on the wallet branch. The incoming conflict blocks modified the legacy demo seed that the wallet branch replaced. Preserved the incoming schema edits outside those blocks, including the household tax snapshot and supplier return updates.

- Solution build passed with 32 warnings and no errors.
- Tests passed for 1,169 unit cases and 104 integration cases. Five integration cases were skipped. The catalog seed test could connect to PostgreSQL, but its database lacked `Products.ProcessedSellingPricePerKg`. The incoming migration and initializer both define this column. The same catalog test passed on the test's InMemory fallback.
- The resolved file and merge notes are staged. The merge commit is pending.
