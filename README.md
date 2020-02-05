# AsyncNats
A Nats.IO client specifically written with new C# features in mind. Internally it uses the new System.IO.Pipelines and System.Threading.Channels libraries that were released last year. It also uses the new IAsyncEnumerable as a way to listen to messages published to subjects.

The end result is very fast Nats.io client that, in our opinion, fits the C# 8.0 language features better than the currently existing libraries.

## Known issues
There are currently no known issues. But the library has not been rigorously tested in production environments yet.

## Known limitations
* No TLS support [and it will probably never be supported]
* Proper documentation, working on it ;)
* The RPC implementation does not support Cancellation tokes (but does obey the Request-timeout as specified by the INatsOptions)
* The RPC implementation does not support overloads, it not work properly with multiple methods with the same name
* The RPC implementation only supports methods
* The RPC implementation is "single threaded" (Well single task)
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
SubscribeAll // This method returns *all* messages that the connection receives but does *not* allow you to supply a subject. Mostly used for debugging.
Subscribe // This method returns messages send to the specified subject but does not perform any deserialization
Subscribe<T> // This method returns deserialized messages send to the specified subject
SubscribeObject<T> // This method is similar to Subscribe<T> but does not wrap the enumerated objects in a NatsTypedMsg, use this if you do not care about subject/subscriptionId/replyTo
SubscribeText // This method is similar to Subscribe<T> except that it only UTF8 decodes the payload
```

The returned subscriptions are AsyncEnumerable objects and can be enumerated using the new await foreach:
```C#
await using var subscription = connection.SubscribeText("HELLO");
await foreach(var message in subscription)
{
	// Process message here
}
```

As the above example shows, the subscriptions also implement AsyncDisposable. You can either use AsyncDisposable or use the Unsubscribe method. The following example is the same as using the await using:
```C#
var subscription = await connection.SubscribeText("HELLO");
//
await connection.Unsubscribe(subscription);
```
Failure to dispose or unsubcribe a subscription means the message queue will fill up and the connection will stop receiving messages!

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