// GP-I10: the Core suite exercises timed file probes and bounded named pipes.
// Avoid CI-host thread contention that would mask their real behavior.
// Individual tests still exercise their own intended internal concurrency.
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 2)]
