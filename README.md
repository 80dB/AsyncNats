# AsyncNats
A Nats.IO client specifically written with new C# features in mind. Internally it uses the new System.IO.Pipelines and System.Threading.Channels libraries that were released last year. It also uses the new IAsyncEnumerable as a way to listen to messages published to subjects.

The end result is very fast Nats.io client that, in our opinion, fits the C# 8.0 language features better than the currently existing libraries.

## Known issues
There are currently no known issues. But the library has not been rigorously tested in production environments yet.

## Shortcomings
* No direct support for RPC
* No TLS support [and it will probably never be supported]
* Proper documentation, working on it ;)
