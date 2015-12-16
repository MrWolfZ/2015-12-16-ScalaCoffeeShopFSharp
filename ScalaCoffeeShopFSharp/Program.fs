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
        scheduleOnce (TimeSpan.FromSeconds 0.5) (mailbox.Sender()) (CoffeePrepared(c, g)) mailbox
    ))

module Guest =
  open Message

  let create system id waiter favCoffee = 

    let actor =
      let run (mailbox: Actor<obj>) =
        mailbox.Defer (fun () -> Logging.logInfo mailbox "Goodbye!")

        let run mailbox coffeeCount msg =
          match msg with
          | CoffeeServed c -> 
            let newCoffeeCount = coffeeCount + 1
            Logging.logInfof mailbox "Enjoying my %d yummy %A!" newCoffeeCount c
            // TODO: read time from config
            scheduleOnce (TimeSpan.FromSeconds 1.0) mailbox.Self CoffeeFinished mailbox
            newCoffeeCount
          | CoffeeFinished ->
            waiter <! Waiter.ServeCoffee favCoffee
            coffeeCount

        typedActorOf3 run 0 mailbox

      spawn system (string id) run
      
    waiter.Tell(Waiter.ServeCoffee favCoffee, actor)
    actor

module CoffeeHouse =
  open Message
  
  let create system = 
    let caffeineLimit = 3 // TODO: read from config

    let run (mailbox: Actor<obj>) =
      let barista = Barista.create mailbox.Context "barista"
      let waiter = Waiter.create mailbox.Context "waiter" mailbox.Self

      Logging.logInfo mailbox "CoffeeHouse Open"

      mailbox.Defer (fun () -> Logging.logInfo mailbox "CoffeeHouse Closed")

      let run _ (guestBook, lastGuestId) msg =
        match msg with
        | CreateGuest c -> 
          let guest = Guest.create mailbox.Context lastGuestId waiter c
          let updatedGuestBook = Map.add guest 0 guestBook
          Logging.logInfof mailbox "Guest %A added to guest book" guest
          (updatedGuestBook, lastGuestId + 1)
        | ApproveCoffee(c, g) ->
          let guestCaffeine = Map.find g guestBook
          if guestCaffeine < caffeineLimit then
            let updatedGuestBook = Map.add g (guestCaffeine + 1) guestBook
            Logging.logInfof mailbox "Guest %A caffeeine count incremented" g
            barista.Tell(PrepareCoffee(c, g), mailbox.Sender())
            (updatedGuestBook, lastGuestId)
          else
            Logging.logInfof mailbox "Sorry, %A, you have reached your limit" g
            mailbox.Context.Stop g
            (guestBook, lastGuestId)

      typedActorOf3 run (Map.empty, 0) mailbox

    spawn system "coffee-house" run

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
