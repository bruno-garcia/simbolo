Code taken from [Ben.Demystifier at 87375e901](https://github.com/benaadams/Ben.Demystifier/tree/87375e9013db462ad5af21bc308bc73c63cfe919).

License [Apache-2](LICENSE).

What I need here is the `method name` rewritten but also be aware of what types were resolved when looking for substitutes for async/await and enumerable state machines.

Code was modified. Ultimately diffing the files to see it all but mainly:
* All types are internal.
* Reading portable PDB from the reader isn't here either. Assumes consumer supports ppdb.
* Nullability check is on so code needed to be adjusted.
* Didn't bring in the `struct` `Enumerable`, just used `IList`.
