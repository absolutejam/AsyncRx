namespace Reaction

type Notification<'a> =
    | OnNext of 'a
    | OnError of exn
    | OnCompleted

[<AutoOpen>]
module Types =
    type Accumulator<'s, 't> = 's -> 't -> 's

    type IAsyncDisposable =
        abstract member DisposeAsync: unit -> Async<unit>

    type IAsyncObserver<'a> =
        abstract member OnNextAsync: 'a -> Async<unit>
        abstract member OnErrorAsync: exn -> Async<unit>
        abstract member OnCompletedAsync: unit -> Async<unit>

    type IAsyncObservable<'a> =
        abstract member SubscribeAsync: IAsyncObserver<'a> -> Async<IAsyncDisposable>

[<AutoOpen>]
module Commands =
    type RefCountCmd =
        | Increase
        | Decrease

    type InnerSubscriptionCmd<'a> =
        | InnerObservable of IAsyncObservable<'a>
        | InnerCompleted of int
        | Completed
        | Dispose

