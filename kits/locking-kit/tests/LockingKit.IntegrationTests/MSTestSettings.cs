// Classes run side by side: every test uses its own keys, and chaos tests that pause a backend use their own container.
[assembly: Parallelize(Scope = ExecutionScope.ClassLevel)]
