# AsyncNats
A Nats.IO client specifically written with new C# features in mind. Internally it uses the new System.IO.Pipelines and System.Threading.Channels libraries that were released last year. It also uses the new IAsyncEnumerable as a way to listen to messages published to subjects.

The end result is very fast Nats.io client that, in our opinion, fits the C# 8.0 language features better than the currently existing libraries.

## Known issues
There are currently no known issues. But the library has not been rigorously tested in production environments yet.

## Known limitations
* No TLS support [and it will probably never be supported]
* Proper documentation, working on it ;)
* The RPC implementation does not support Cancellation tokens (but does obey the Request-timeout as specified by the INatsOptions)
* The RPC implementation does not support overloads, it does not work properly with multiple methods with the same name
* The RPC implementation only supports methods, it does not support properties or fields
* The RPC implementation does not support generic methods
* Remote exceptions do not include the remote stack trace and *might* fail if the Exception is not serializable by BinaryFormatter

## Usage
You can publish messages using any of the following methods:
```C#
PublishObjectAsync // This method serializes the object with the supplied/default serializer
PublishTextAsync // This method publishes a raw UTF8 encoded string
PublishAsync // This method publishes a raw byte array
PublishMemoryAsync // This method publishes a raw byte array (in the form of Memory<byte>)
```

You can subscribe to subjects with the following methods:
```C#
Subscribe // This method returns messages send to the specified subject but does not perform any deserialization, *the payload is only valid until the next message has been enumerated*
Subscribe<T> // This method returns deserialized messages send to the specified subject
SubscribeObject<T> // This method is similar to Subscribe<T> but does not wrap the enumerated objects in a NatsTypedMsg, use this if you do not care about subject/subscriptionId/replyTo
SubscribeText // This method is similar to Subscribe<T> except that it only UTF8 decodes the payload
```

The returned subscriptions are AsyncEnumerables and can be enumerated using the new await foreach:
```C#
await foreach(var message in connection.SubscribeText("HELLO"))
{
	// Process message here
}
```

There's also the option to perform requests using the following methods:
```C#
Request // This method sends and receives a raw byte[]
RequestMemory // This method sends and receives a raw byte[] in the form of Memory
RequestText // This method sends and receives a UTF8 string
RequestObject // This method sends and receives a serialized/deserialized object 
```

The request methods require a process to listen to the subjects. The replyTo-subject is automatically generated using the Environment.TickCount when the connection options where created and an internal counter. In larger setups where multiple processes are starting at the same time this might not be unique enough. You can change this prefix by changing it in the options when creating a NatsConnection.

## RPC Usage
You can let AsyncNats handle RPC calls for you (instead of using Request + Subscribe) by using these two methods:
```C#
StartContractServer<TContract>
GenerateContractClient<TContract>
```

The contract has to be an interface and only supports methods (both sync/async). The InterfaceAsyncNatsSample gives a good idea on how to  use them. 

It's possible to have multiple contract servers running with a different base subject. This feature is still in experimental phase.

## Release history

### v1.0.2
* Breaking changes:
  * Removed the PublishMemoryAsync/PublishTextAsync methods in favour of a single PublishAsync that handles both string and byte[] types
  * Removed the RequestMemory/RequestText methods in favour of a single Request that handles both string and byte[] types
  * Removed various added PublishAsync overrides in favour of a single PublishAsync with optional parameters
* Overridden NatsKey.ToString for logging purposes
* Added header support to Request methods
* Note: if headers are passed to any function it will always do a HPUB, if headers are null it will always do a PUB

### v1.0.1
* @israellot improved performance / memory allocations
* @israellot added a more realistic benchmark
* @israellot added header support 

### v1.0.0
* Requests will only use a single subscription instead of setting up a new subscription every request/response
* Moved request/response handling to it's own separate class
* Added ILoggerFactory support, RPC is logged, raw request/response/publish is not
* Bumped to v1.0.0 - This library has been tested in a production environment that handles 50-100k messages/sec

### v0.8.5
* Added ReceivedBytesTotal and TransmitBytesTotal as properties to more monitor connection
* Added SenderQueueSize, ReceiverQueueSize, ReceivedBytesTotal and TransmitBytesTotal to interface

### v0.8.4
* Timeout on RPC client did not work as intended would could make the caller hang indefinitely if the server did not respond

### v0.8.3
* Publish (PUB) was reserving one byte too much when payload was 9, 99, 999, 9999, 99999, 999999 bytes large
* Unsubscribe (UNSUB) was reserving one byte too much when max messages was 9, 99, 999, 9999, 99999, 999999 bytes large

### v0.8.2
* Refactored Subscribe a little bit
* Added a missing memory-rent which could corrupt memory in high message volumes
* Changed from ArrayPool to a custom MemoryPool to improve overal performance

### v0.8.1
* Upgraded dependencies
* Fixed a small issue with System.IO.Pipelines

### v0.8.0
* Breaking change: Rewrote subscriptions to make them a bit easier to use
* Breaking change: Removed "SubscribeAll", it made the process loop more difficult and wasn't of much use
* Slightly increased performance of message process loop
* The RPC Server proxy would eat exceptions if tasks got executed by the task scheduler/factory
* All deserialize and RPC server exceptions are now passed to ConnectionException event handler

### v0.7.1
* Pipe did not support multiple simultaneous WriteAsync's, rewrote to use Channel instead with an internal 1Mb socket buffer (it's actually faster)

### v0.7.0
* Reduced amount of queue's inside the connection
* Made amount of queue'd bytes visible in SenderQueueSize and ReceiverQueueSize properties
* Added CancellationToken to internal publish methods

### v0.6.5
* Fixed an issue where the send/receive loop task would get executed synchroniously instead of asynchroniously

### v0.6.4
* Added optional TaskScheduler parameter to StartContractServer to make the "Server" run task concurrently
* Added CancellationToken to all Async methods

### v0.6.3
* Added fire and forget methods (add NatsFireAndForget attribute to the methods), the caller doesn't wait for an answer. Note, exceptions thrown inside fire and forget methods will be lost!
* An exception will be thrown when ValueTask is used as a contract type

### v0.6.2
* Updated InterfaceAsyncNatsSample to use a custom serializer (MessagePack)
* Fixed an issue when MessagePack was used as serializer (and possible others)
* Added DataContract / DataMember attributes to request/response classes used by the RPC functionality to aid MessagePack (and possible others)

### v0.6.1
* Forgot to add StartContractServer to the interface
* Dispose the contract server channel once done (due to cancellation or exception)

### v0.6
* Added RPC functionality using interface contracts (see InterfaceAsyncNatsSample)

### v0.5.2
* Increased pauseWriterThreshold on receiver pipe to 1Mb to correctly handle large messages

### v0.5.1
* Added events and status to INatsConnection interface

### v0.5
* Added (simple) Request-Reply pattern
* Added Status property to get current connection status
* Added ConnectionException event
* Added StatusChange event 
* Added ConnectionInformation event

### v0.4
* Resolved a Dispose exception
* Added SubscribeObject method

### v0.3
* Added PublishText / SubscribeText methods

### v0.2
* Added some missing fields to connect

### v0.1
* Initial release