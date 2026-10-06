using Xunit;

// WPF Application and Settings.Default are process-wide resources shared by these tests.
// Isolate test cases; explicit concurrency exercised inside individual tests is unchanged.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
