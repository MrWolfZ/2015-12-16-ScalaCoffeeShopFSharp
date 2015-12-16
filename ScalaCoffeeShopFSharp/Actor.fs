module Actor

open Akka.Actor
open Akka.FSharp

type SystemMessage =
| Terminated of Terminated

let private handleUntypedMessage<'Message, 'State> fn sysFn (state: 'State) (mailbox: Actor<obj>) =
  actor {
    let! (message: obj) = mailbox.Receive()
    match message with
    | :? 'Message as m ->
      return fn mailbox state m
    | :? Terminated as t -> 
      return sysFn mailbox state (Terminated t)
    | _ -> 
      mailbox.Context.System.DeadLetters <! message
      return state
  }

let typedActorOf4<'Message, 'State> fn sysFn (initialState: 'State) (mailbox: Actor<obj>) = 
      let rec loop state = 
        actor { 
          let! newState = handleUntypedMessage<'Message, 'State> fn sysFn state mailbox
          return! loop newState
        }
      loop initialState

let typedActorOf3<'Message, 'State> fn (initialState: 'State) (mailbox: Actor<obj>) = 
  typedActorOf4<'Message, 'State> fn (fun _ s _ -> s) initialState mailbox

let typedActorOf2<'Message> fn (mailbox: Actor<obj>) = 
  typedActorOf3<'Message, unit> (fun m _ msg -> fn m msg) () mailbox

let typedActorOf<'Message> fn (mailbox: Actor<obj>) = 
  typedActorOf2<'Message> (fun _ msg -> fn msg) mailbox

let scheduleOnce delay receiver message (actor: Actor<'T>) =
  actor.Context.System.Scheduler.ScheduleTellOnce(delay, receiver, message)

