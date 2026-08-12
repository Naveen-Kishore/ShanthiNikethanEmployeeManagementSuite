using Xunit;

// By default, xUnit runs different test CLASSES concurrently (tests
// within the same class already run sequentially, since they share one
// class-level PlaywrightFixture). As this suite has grown - now 27 tests
// across 6 classes - that concurrency became the actual source of
// several rounds of confusing, transient timeouts: multiple test classes
// each launching their own browser, each signing in, each hitting the
// same single app instance and SQL Server database at once.
//
// This trades some total run time for consistently trustworthy results -
// a test suite that occasionally needs re-running to get a clean pass is
// less useful than a slower one that just works the first time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
