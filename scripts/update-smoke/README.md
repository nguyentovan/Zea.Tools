# Verify a portable update

Run from the repository root on Windows with .NET 10. Restore the pinned Velopack CLI before running the script.

```powershell
dotnet tool restore
./scripts/update-smoke/Verify-PortableUpdate.ps1
```

A successful run prints `PASS Portable update 0.0.1 to 0.0.2` and the evidence directory. The directory contains `result.json`, `events.jsonl`, package logs, and the tested source snapshot with its SHA256 hash. Keep these files as Windows CI artifacts.

The script builds a separate executable named `Zea.Tweakr.UpdateSmoke`. The probe links the production `AppUpdater.cs` and uses real Velopack update managers. Both packages use the production package identity so the production identity check runs unchanged. Extraction, packages, and settings stay inside a unique run directory. The script does not create an installer or shortcuts.

The probe checks the extracted portable layout, the no-update result, an unavailable network feed, a same-size corrupted download, a successful download, graceful apply, and restart into the new assembly. It compares the settings sentinel's exact bytes before and after the restart. A source hash check rejects a run if the production updater changes while the test runs.

Use `OutputDirectory`, `PackagesDirectory`, and `TimeoutSeconds` to change the generated-output location, NuGet cache, or restart deadline. Every run uses a new directory and preserves the previous evidence.
