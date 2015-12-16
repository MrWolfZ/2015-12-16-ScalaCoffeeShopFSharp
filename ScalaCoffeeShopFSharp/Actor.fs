module Actor

open Akka.FSharp

let private handleUntypedMessage<'Message, 'State> fn (state: 'State) (mailbox: Actor<obj>) =
  actor {
    let! (message: obj) = mailbox.Receive()
    match message with
    | :? 'Message as m ->
      return fn mailbox state m
    | _ -> 
      mailbox.Context.System.DeadLetters <! message
      return state
  }

let typedActorOf3<'Message, 'State> fn (initialState: 'State) (mailbox: Actor<obj>) = 
      let rec loop state = 
        actor { 
          let! newState = handleUntypedMessage<'Message, 'State> fn state mailbox
          return! loop newState
        }
      loop initialState

let typedActorOf2<'Message> fn (mailbox: Actor<obj>) = 
  typedActorOf3<'Message, unit> (fun m _ msg -> fn m msg) () mailbox

let typedActorOf<'Message> fn (mailbox: Actor<obj>) = 
  typedActorOf2<'Message> (fun _ msg -> fn msg) mailbox

