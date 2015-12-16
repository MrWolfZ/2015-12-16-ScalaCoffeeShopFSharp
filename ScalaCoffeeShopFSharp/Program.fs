module Program

open System
open Actor
open Akka.Actor
open Akka.FSharp

type Coffee =
| Akkaccino
| MochaPlay
| CaffeScala

module Message =
  type CoffeeHouse =
  | CreateGuest of favCoffee: Coffee
  | ApproveCoffee of coffee: Coffee * guest: IActorRef

  type Guest =
  | CoffeeServed of coffee: Coffee
  | CoffeeFinished

  type Waiter =
  | ServeCoffee of coffee: Coffee
  | CoffeePrepared of coffee: Coffee * guest: IActorRef

  type Barista =
  | PrepareCoffee of coffee: Coffee * guest: IActorRef

module Waiter =
  open Message
  let create system name coffeeHouse =
    spawn system name (typedActorOf2 (fun mailbox msg ->
      match msg with
      | ServeCoffee c -> coffeeHouse <! ApproveCoffee (c, mailbox.Sender())
      | CoffeePrepared(c, g) -> g <! Guest.CoffeeServed c
    ))

module Barista =
  open Message
  let create system name =
    spawn system name (typedActorOf2 (fun mailbox msg ->
      match msg with
      | PrepareCoffee (c, g) -> 
        // TODO: read time from config
        mailbox.Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(1.0), mailbox.Sender(), CoffeePrepared(c, g))
    ))

module Guest =
  open Message
  let create system id waiter favCoffee = 
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
        waiter <! Waiter.ServeCoffee favCoffee
        coffeeCount
      ) 
      0)
      
    waiter.Tell(Waiter.ServeCoffee favCoffee, actor)
    actor

module CoffeeHouse =
  open Message
  
  let create system = 
    let caffeineLimit = 5 // TODO: read from config
    spawn system "coffee-house" (fun mailbox -> 
      let barista = Barista.create mailbox.Context "barista"
      let waiter = Waiter.create mailbox.Context "waiter" mailbox.Self

      Logging.logDebug mailbox "CoffeeHouse Open"

      mailbox.Defer (fun () -> Logging.logDebug mailbox "CoffeeHouse Closed")

      typedActorOf3
        (fun mailbox (guestBook, lastGuestId) msg ->
          match msg with
          | CreateGuest c -> 
            let guest = Guest.create mailbox.Context lastGuestId waiter c
            Logging.logInfof mailbox "Guest %A added to guest book" guest
            (Map.add guest 0 guestBook, lastGuestId + 1)
          | ApproveCoffee(c, g) ->
            barista.Tell(PrepareCoffee(c, g), mailbox.Sender())
            (guestBook, lastGuestId)
        ) (Map.empty, 0) mailbox
    )

let run() =
  let config = Configuration.load()
  use system = System.create "my-system" config

  let coffeeHouse = CoffeeHouse.create system

  coffeeHouse <! Message.CreateGuest Akkaccino
  coffeeHouse <! Message.CreateGuest CaffeScala

  Console.ReadKey() |> ignore

[<EntryPoint>]
let main argv = 
  run()
  Console.ReadKey() |> ignore
  0
