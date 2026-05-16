// Shared state lives in ChatLogScanner / LocalizationController; serialize all
// tests so parallel execution doesn't trample the static state.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
