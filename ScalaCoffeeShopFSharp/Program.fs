module Program

open System
open Actor
open Akka.Actor
open Akka.FSharp

module Coffee =
  type T =
  | Akkaccino
  | MochaPlay
  | CaffeScala

  let private getRandArrElement =
    let rnd = Random()
    fun (arr : T[]) -> arr.[rnd.Next(arr.Length)]

  let anyOther c =
    Set.ofList [Akkaccino; MochaPlay; CaffeScala]
    |> Set.remove c
    |> Array.ofSeq
    |> getRandArrElement

module Message =
  type CoffeeHouse =
  | CreateGuest of favCoffee: Coffee.T * caffeineLimit: int
  | ApproveCoffee of coffee: Coffee.T * guest: IActorRef

  type Guest =
  | CoffeeServed of coffee: Coffee.T
  | CoffeeFinished

  type Waiter =
  | ServeCoffee of coffee: Coffee.T
  | CoffeePrepared of coffee: Coffee.T * guest: IActorRef
  | Complaint of coffee: Coffee.T

  type Barista =
  | PrepareCoffee of coffee: Coffee.T * guest: IActorRef

module Waiter =
  open Message

  type FrustratedException() =
    inherit InvalidOperationException("Waiter is frustrated!")

  let create system name coffeeHouse barista maxComplaintCount =
    spawn system name (typedActorOf3 (fun mailbox complaintCount msg ->
      match msg with
      | ServeCoffee c -> 
        coffeeHouse <! ApproveCoffee (c, mailbox.Sender())
        complaintCount
      | CoffeePrepared(c, g) -> 
        g <! Guest.CoffeeServed c
        complaintCount
      | Complaint(c) when complaintCount >= maxComplaintCount ->
        raise <| new FrustratedException()
      | Complaint(c) ->
        barista <! PrepareCoffee(c, mailbox.Sender())
        complaintCount + 1
    ) 0)

module Barista =
  open Message

  let create system name (prepareCoffeeDuration: TimeSpan) accuracy =
    let run (mailbox: Actor<obj>) =
      let r = new Random()

      let run _ msg =
        match msg with
        | PrepareCoffee (c, g) -> 
          let preparedCoffee = if r.Next 100 < accuracy then c else Coffee.anyOther c
          
          mailbox.Sender() <! Async.RunSynchronously(async {
            do! Async.Sleep (int prepareCoffeeDuration.TotalMilliseconds)
            return CoffeePrepared(preparedCoffee, g)
          })

      typedActorOf2 run mailbox

    spawn system name run

module Guest =
  open Message

  type CaffeineException() =
    inherit InvalidOperationException("Too much caffeine!")

  let create system waiter favCoffee finishCoffeeDuration caffeineLimit = 

    let actor =
      let run (mailbox: Actor<obj>) =
        mailbox.Defer (fun () -> Logging.logInfo mailbox "Goodbye!")
        waiter <! Waiter.ServeCoffee favCoffee

        let run mailbox coffeeCount msg =
          match msg with
          | CoffeeServed c when c <> favCoffee ->
            Logging.logInfof mailbox "Expected a %A, but got a %A!" favCoffee c
            waiter <! Complaint(favCoffee)
            coffeeCount
          | CoffeeServed c -> 
            let newCoffeeCount = coffeeCount + 1
            Logging.logInfof mailbox "Enjoying my %d yummy %A!" newCoffeeCount c
            scheduleOnce finishCoffeeDuration mailbox.Self CoffeeFinished mailbox
            newCoffeeCount
          | CoffeeFinished ->
            if coffeeCount > caffeineLimit then
              raise <| new CaffeineException()

            waiter <! Waiter.ServeCoffee favCoffee
            coffeeCount

        typedActorOf3 run 0 mailbox

      spawn system null run
      
    actor

module CoffeeHouse =
  open Message
  
  let create system caffeineLimit = 
    let run (mailbox: Actor<obj>) =
    
      let finishCoffeeDuration = mailbox.Context.System.Settings.Config.GetTimeSpan "coffee-house.guest.finish-coffee-duration"
      let prepareCoffeeDuration = mailbox.Context.System.Settings.Config.GetTimeSpan "coffee-house.barista.prepare-coffee-duration"
      let baristaAccuracy = mailbox.Context.System.Settings.Config.GetInt "coffee-house.barista.accuracy"
      let maxComplaintCount = mailbox.Context.System.Settings.Config.GetInt "coffee-house.waiter.max-complaint-count"

      let barista = Barista.create mailbox.Context "barista" prepareCoffeeDuration baristaAccuracy
      let waiter = Waiter.create mailbox.Context "waiter" mailbox.Self barista maxComplaintCount

      Logging.logInfo mailbox "CoffeeHouse Open"

      mailbox.Defer (fun () -> Logging.logInfo mailbox "CoffeeHouse Closed")

      let run _ guestBook msg =
        match msg with
        | CreateGuest (c, l) -> 
          let guest = Guest.create mailbox.Context waiter c finishCoffeeDuration l
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
      | _ -> SupervisorStrategy.DefaultDecider.Decide ex

    spawnOpt system "coffee-house" run [
      SpawnOption.SupervisorStrategy(Strategy.OneForOne decider)
    ]
    
open Coffee

let run() =

  let config = Configuration.load()
  use system = System.create "my-system" config
  let caffeineLimit = system.Settings.Config.GetInt "coffee-house.caffeine-limit"

  let coffeeHouse = CoffeeHouse.create system caffeineLimit

  coffeeHouse <! Message.CreateGuest(Akkaccino, 2)
  // coffeeHouse <! Message.CreateGuest(CaffeScala, 2)

  Console.ReadKey() |> ignore

[<EntryPoint>]
let main argv = 
  run()
  Console.ReadKey() |> ignore
  0
