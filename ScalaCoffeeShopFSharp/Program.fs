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
  | CreateGuest of favCoffee: Coffee * caffeineLimit: int
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
        mailbox.Sender() <! Async.RunSynchronously(async {
          do! Async.Sleep 500
          return CoffeePrepared(c, g)
        })
    ))

module Guest =
  open Message

  type CaffeineException() =
    inherit InvalidOperationException("Too much caffeine!")

  let create system waiter favCoffee caffeineLimit = 

    let actor =
      let run (mailbox: Actor<obj>) =
        mailbox.Defer (fun () -> Logging.logInfo mailbox "Goodbye!")
        waiter <! Waiter.ServeCoffee favCoffee

        let run mailbox coffeeCount msg =
          match msg with
          | CoffeeServed c -> 
            let newCoffeeCount = coffeeCount + 1
            Logging.logInfof mailbox "Enjoying my %d yummy %A!" newCoffeeCount c
            // TODO: read time from config
            scheduleOnce (TimeSpan.FromSeconds 1.0) mailbox.Self CoffeeFinished mailbox
            newCoffeeCount
          | CoffeeFinished ->
            if coffeeCount > caffeineLimit then
              raise (new CaffeineException())

            waiter <! Waiter.ServeCoffee favCoffee
            coffeeCount

        typedActorOf3 run 0 mailbox

      spawn system null run
      
    actor

module CoffeeHouse =
  open Message
  
  let create system = 
    let caffeineLimit = 5 // TODO: read from config

    let run (mailbox: Actor<obj>) =
      let barista = Barista.create mailbox.Context "barista"
      let waiter = Waiter.create mailbox.Context "waiter" mailbox.Self

      Logging.logInfo mailbox "CoffeeHouse Open"

      mailbox.Defer (fun () -> Logging.logInfo mailbox "CoffeeHouse Closed")

      let run _ guestBook msg =
        match msg with
        | CreateGuest (c, l) -> 
          let guest = Guest.create mailbox.Context waiter c l
          let updatedGuestBook = Map.add guest 0 guestBook
          Logging.logInfof mailbox "Guest %A added to guest book" guest
          monitor guest mailbox |> ignore
          updatedGuestBook
        | ApproveCoffee(c, g) ->
          let guestCaffeine = Map.find g guestBook
          if guestCaffeine < caffeineLimit then
            let updatedGuestBook = Map.add g (guestCaffeine + 1) guestBook
            Logging.logInfof mailbox "Guest %A caffeeine count incremented" g
            barista.Forward(PrepareCoffee(c, g))
            updatedGuestBook
          else
            Logging.logInfof mailbox "Sorry, %A, you have reached your limit" g
            mailbox.Context.Stop g
            guestBook

      let runSystem _ guestBook sysMsg =
        match sysMsg with
        | Terminated t ->
          Logging.logInfof mailbox "Thanks %A, for being our guest!" t.ActorRef
          Map.remove t.ActorRef guestBook

      typedActorOf4 run runSystem Map.empty mailbox

    let decider (ex: exn) = 
      match ex with 
      | :? Guest.CaffeineException -> Directive.Stop 
      | _ -> Directive.Restart

    spawnOpt system "coffee-house" run [
      SpawnOption.SupervisorStrategy(Strategy.OneForOne decider)
    ]

let run() =
  let config = Configuration.load()
  use system = System.create "my-system" config

  let coffeeHouse = CoffeeHouse.create system

  coffeeHouse <! Message.CreateGuest(Akkaccino, 2)
  coffeeHouse <! Message.CreateGuest(CaffeScala, 2)

  Console.ReadKey() |> ignore

[<EntryPoint>]
let main argv = 
  run()
  Console.ReadKey() |> ignore
  0
