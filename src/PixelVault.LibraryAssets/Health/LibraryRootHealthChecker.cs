namespace PixelVault.LibraryAssets.Health;

/// <summary>Preflight checks before applying scan reconciliation that could drop or hard-delete data.</summary>
public static class LibraryRootHealthChecker
{
    static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <param name="enumerateFiles">Optional full-library file pass for sanity checks; if null, file-count checks are skipped.</param>
    public static LibraryRootHealthResult Check(
        string libraryRoot,
        LibraryRootHealthOptions options,
        LibraryRootHealthContext? context,
        Func<string, CancellationToken, IEnumerable<string>>? enumerateFiles,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var opts = options ?? new LibraryRootHealthOptions();

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return Fail(LibraryRootHealthFailureCode.Offline, "Library root is empty.", messages);
        }

        string fullRoot;
        try { fullRoot = Path.GetFullPath(libraryRoot.Trim()); }
        catch (Exception ex)
        {
            messages.Add(ex.Message);
            return Fail(LibraryRootHealthFailureCode.Offline, "Library root path is invalid.", messages);
        }

        if (!Directory.Exists(fullRoot))
        {
            messages.Add("Directory does not exist: " + fullRoot);
            return Fail(LibraryRootHealthFailureCode.Offline, "Library offline or path missing.", messages);
        }

        try
        {
            using var _ = Directory.EnumerateFileSystemEntries(fullRoot).GetEnumerator();
            _.MoveNext();
        }
        catch (Exception ex)
        {
            messages.Add(ex.Message);
            return Fail(LibraryRootHealthFailureCode.NotReadable, "Library root is not readable.", messages);
        }

        if (opts.ExpectedTopLevelFolderNames is { Count: > 0 })
        {
            string[] children;
            try
            {
                children = Directory.GetDirectories(fullRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrEmpty(n)).ToArray()!;
            }
            catch (Exception ex)
            {
                messages.Add(ex.Message);
                return Fail(LibraryRootHealthFailureCode.NotReadable, "Could not list top-level folders.", messages);
            }

            var set = new HashSet<string>(children, PathComparer);
            foreach (var expected in opts.ExpectedTopLevelFolderNames)
            {
                if (string.IsNullOrWhiteSpace(expected)) continue;
                var name = expected.Trim();
                if (set.Contains(name)) continue;
                messages.Add("Expected top-level folder not found: " + name);
                return Fail(LibraryRootHealthFailureCode.TopologyMismatch, "Expected folder topology not present.", messages);
            }
        }

        int observed = 0;
        if (enumerateFiles != null && (opts.MinimumAbsoluteFileCount is not null
                                       || opts.MinimumFractionOfLastHealthyCount is not null
                                       || context?.LastHealthyObservedFileCount is not null))
        {
            var timeout = opts.EnumerationTimeout ?? TimeSpan.FromMinutes(2);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(timeout);
            try
            {
                foreach (var _ in enumerateFiles(fullRoot, linked.Token)) observed++;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                messages.Add("Enumeration timed out after " + timeout);
                return Fail(LibraryRootHealthFailureCode.NotReadable, "Library enumeration timed out.", messages, observed);
            }
            catch (Exception ex)
            {
                messages.Add(ex.Message);
                return Fail(LibraryRootHealthFailureCode.NotReadable, "Library enumeration failed.", messages, observed);
            }

            if (opts.MinimumAbsoluteFileCount is { } minAbs && observed < minAbs)
            {
                messages.Add($"Observed file count {observed} is below minimum {minAbs}.");
                return Fail(LibraryRootHealthFailureCode.SanityFileCount, "File-count sanity check failed.", messages, observed);
            }

            if (context?.LastHealthyObservedFileCount is { } last && last > 0 && opts.MinimumFractionOfLastHealthyCount is { } frac)
            {
                var required = (int)Math.Floor(last * frac);
                if (required < 1) required = 1;
                if (observed < required)
                {
                    messages.Add($"Observed {observed} files; require at least {required} ({frac:P0} of last healthy {last}).");
                    return Fail(LibraryRootHealthFailureCode.SanityFileCount, "File-count sanity check failed.", messages, observed);
                }
            }
        }

        return new LibraryRootHealthResult { IsHealthy = true, Messages = messages, ObservedFileCount = observed };
    }

    static LibraryRootHealthResult Fail(string code, string summary, List<string> messages, int observed = 0)
    {
        if (!messages.Contains(summary, StringComparer.Ordinal)) messages.Insert(0, summary);
        return new LibraryRootHealthResult
        {
            IsHealthy = false,
            FailureCode = code,
            Messages = messages,
            ObservedFileCount = observed
        };
    }
}
