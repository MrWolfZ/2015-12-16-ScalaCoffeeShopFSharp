module Program

open System
open Akka.Actor
open Akka.FSharp

type Coffee =
| Akkaccino
| MochaPlay
| CaffeScala

type CoffeeHouseMessage =
| CreateGuest of favCoffee: Coffee
| ApproveCoffee of coffee: Coffee * guest: IActorRef

type GuestMessage =
| CoffeeServed of coffee: Coffee
| CoffeeFinished

type BaristaMessage =
| PrepareCoffee of coffee: Coffee * guest: IActorRef

type WaiterMessage =
| ServeCoffee of coffee: Coffee
| CoffeePrepared of coffee: Coffee * guest: IActorRef

let handleUntypedMessage<'Message, 'State> fn (state: 'State) (mailbox: Actor<obj>) =
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

let run() =
  let config = Configuration.load()
  use system = System.create "my-system" config

  let createGuest system id waiter favCoffee = 
    let actor = 
      spawn system (string id) (typedActorOf3 (fun mailbox coffeeCount msg ->
        match msg with
        | CoffeeServed c -> 
          let newCoffeeCount = coffeeCount + 1
          Logging.logInfof mailbox "Enjoying my %d yummy %A!" newCoffeeCount c
          // TODO: read time from config
          mailbox.Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(2.0), mailbox.Self, CoffeeFinished)   
          newCoffeeCount
        | CoffeeFinished ->
          waiter <! ServeCoffee favCoffee
          coffeeCount
        ) 
      0)
      
    waiter.Tell(ServeCoffee favCoffee, actor)
    actor

  let createBarista system name =
    spawn system name (typedActorOf2 (fun mailbox msg ->
      match msg with
      | PrepareCoffee (c, g) -> 
        // TODO: read time from config
        mailbox.Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(1.0), mailbox.Sender(), CoffeePrepared(c, g))
    ))

  let createWaiter system name coffeeHouse =
    spawn system name (typedActorOf2 (fun mailbox msg ->
      match msg with
      | ServeCoffee c -> coffeeHouse <! ApproveCoffee (c, mailbox.Sender())
      | CoffeePrepared(c, g) -> g <! CoffeeServed c
    ))
  
  let coffeeHouse = 
    let caffeineLimit = 5 // TODO: read from config
    spawn system "coffee-house" (fun mailbox -> 
      let barista = createBarista mailbox.Context "barista"
      let waiter = createWaiter mailbox.Context "waiter" mailbox.Self

      Logging.logDebug mailbox "CoffeeHouse Open"

      mailbox.Defer (fun () -> Logging.logDebug mailbox "CoffeeHouse Closed")

      typedActorOf3
        (fun mailbox (guestBook, lastGuestId) msg ->
          match msg with
          | CreateGuest c -> 
            let guest = createGuest mailbox.Context lastGuestId waiter c
            Logging.logInfof mailbox "Guest %A added to guest book" guest
            (Map.add guest 0 guestBook, lastGuestId + 1)
          | ApproveCoffee(c, g) ->
            barista.Tell(PrepareCoffee(c, g), mailbox.Sender())
            (guestBook, lastGuestId)
        ) (Map.empty, 0) mailbox
    )

  coffeeHouse <! CreateGuest Akkaccino
  coffeeHouse <! CreateGuest CaffeScala

  Console.ReadKey() |> ignore

[<EntryPoint>]
let main argv = 
  run()
  Console.ReadKey() |> ignore
  0
