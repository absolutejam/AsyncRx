namespace FSharp.Control

open System.Collections.Generic
open System.Threading

open Core

module Subjects =
    /// A cold stream that only supports a single subscriber
    let singleSubject<'a> () : IAsyncObserver<'a> * IAsyncObservable<'a> =
        let mutable oobv: IAsyncObserver<'a> option = None
        let cts = new CancellationTokenSource ()

        let subscribeAsync (aobv : IAsyncObserver<'a>) : Async<IAsyncDisposable> =
            let sobv = safeObserver aobv
            if Option.isSome oobv then
                failwith "singleStream: Already subscribed"

            oobv <- Some sobv
            cts.Cancel ()

            async {
                let cancel () = async {
                    oobv <- None
                }
                return AsyncDisposable.Create cancel
            }

        let obv (n: Notification<'a>) =
            async {
                while oobv.IsNone do
                    // Wait for subscriber
                    Async.StartImmediate (Async.Sleep 100, cts.Token)

                match oobv with
                | Some obv ->
                    match n with
                    | OnNext x ->
                        try
                            do! obv.OnNextAsync x
                        with ex ->
                            do! obv.OnErrorAsync ex
                    | OnError e -> do! obv.OnErrorAsync e
                    | OnCompleted -> do! obv.OnCompletedAsync ()
                | None ->
                    printfn "No observer for %A" n
                    ()
            }
        let obs = { new IAsyncObservable<'a> with member __.SubscribeAsync o = subscribeAsync o }
        AsyncObserver obv :> IAsyncObserver<'a>, obs

    /// A mailbox subject is a subscribable mailbox. Each message is
    /// broadcasted to all subscribed observers.
    let mbSubject<'a> () : MailboxProcessor<Notification<'a>>*IAsyncObservable<'a> =
        let obvs = new List<IAsyncObserver<'a>>()
        let cts = new CancellationTokenSource()

        let mb = MailboxProcessor.Start(fun inbox ->
            let rec messageLoop _ = async {
                let! n = inbox.Receive ()

                for aobv in obvs do
                    match n with
                    | OnNext x ->
                        try
                            do! aobv.OnNextAsync x
                        with ex ->
                            do! aobv.OnErrorAsync ex
                            cts.Cancel ()
                    | OnError err ->
                        do! aobv.OnErrorAsync err
                        cts.Cancel ()
                    | OnCompleted ->
                        do! aobv.OnCompletedAsync ()
                        cts.Cancel ()

                return! messageLoop ()
            }
            messageLoop ()
        , cts.Token)

        let subscribeAsync (aobv: IAsyncObserver<'a>) : Async<IAsyncDisposable> =
            async {
                let sobv = safeObserver aobv
                obvs.Add sobv

                let cancel () = async {
                    obvs.Remove sobv |> ignore
                }
                return AsyncDisposable.Create cancel
            }

        mb, { new IAsyncObservable<'a> with member __.SubscribeAsync o = subscribeAsync o }

    /// A stream is both an observable sequence as well as an observer.
    /// Each notification is broadcasted to all subscribed observers.
    let subject<'a> () : IAsyncObserver<'a> * IAsyncObservable<'a> =
        let mb, obs = mbSubject<'a> ()

        let obv = { new IAsyncObserver<'a> with
            member this.OnNextAsync x = async {
                OnNext x |> mb.Post
            }
            member this.OnErrorAsync err = async {
                OnError err |> mb.Post
            }
            member this.OnCompletedAsync () = async {
                OnCompleted |> mb.Post
            }
        }

        obv, obs