using Xunit;

#if GITHUB_ACTIONS
[assembly: CollectionBehavior(DisableTestParallelization = true)]
#endif
