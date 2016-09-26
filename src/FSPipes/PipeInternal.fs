(*
   Copyright 2015-2016 Philip Curzon

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*)

namespace NovelFS.FSPipes

open NovelFS.NovelIO

type X = private |Closed

type Pipeline<'UO, 'UI, 'DI, 'DO, 'V> =
    |Request of 'UO * ('UI -> Pipeline<'UO, 'UI, 'DI, 'DO, 'V>)
    |Respond of 'DO * ('DI -> Pipeline<'UO, 'UI, 'DI, 'DO, 'V>)
    |IOM of IO<Pipeline<'UO, 'UI, 'DI, 'DO, 'V>>
    |Value of 'V

type Producer<'DO, 'V> = Pipeline<X, unit, unit, 'DO, 'V>

type Consumer<'UI, 'V> = Pipeline<unit, 'UI, unit, X, 'V>

type Pipe<'UI, 'DO, 'V> = Pipeline<unit, 'UI, unit, 'DO, 'V>

type Effect<'V> = Pipeline<X, unit, unit, X, 'V>

module Pipeline =
    // ------------ CATEGORY IDENTITIES ------------

    let return' v = Value v

    let respond a = Respond (a, Value)

    let request x = Request (x, Value)

    let rec pull p' = Request (p', (fun p -> Respond (p, pull)))

    let rec push p = Respond (p, fun p' -> Request(p', push))


    // ------------ CATEGORY COMPOSITION ------------

    let bind p0 f = 
        let rec bindRec p = 
            match p with
            |Request (a', fa) -> Request (a', bindRec << fa)
            |Respond (b,  fb') -> Respond (b, bindRec << fb')
            |IOM io -> IOM <| IO.map bindRec io
            |Value r -> f r
        bindRec p0

    let bindResponse p0 fb = 
        let rec bindRec p = 
            match p with
            |Request (x', fx)  -> Request (x', bindRec << fx)
            |Respond (b, fb') -> bind (fb b) (bindRec << fb')
            |IOM io -> IOM <| IO.map bindRec io
            |Value a   -> Value a
        bindRec p0

    let bindRequest p0 fb' = 
        let rec bindRec p = 
            match p with
            |Request (b', fb)  -> bind (fb' b') (bindRec << fb)
            |Respond (x, fx') -> Respond (x, bindRec << fx')
            |IOM io -> IOM <| IO.map bindRec io
            |Value r -> Value r
        bindRec p0

    let rec bindPush p fb =
        match p with
        |Request (a', fa)  -> Request (a', (fun a -> bindPush (fa a) fb))
        |Respond (b , fb') ->  bindPull (fb b) fb'
        |IOM io -> IOM <| IO.map (fun p' -> bindPush p' fb) io
        |Value r   -> Value r

    and bindPull p fb' =
        match p with
        |Request (b', fb)  -> bindPush (fb' b') fb
        |Respond (c,  fc') -> Respond (c, (fun c' -> bindPull (fc' c') fb' ))
        |IOM io -> IOM <| IO.map (fun p' -> bindPull p' fb') io
        |Value r   -> Value r

    // Builder

    type PipeBuilder() =
        member this.Return x = return' x
        member this.ReturnFrom x : Pipeline<_,_,_,_,_> = x
        member this.Bind (x, f) = bind x f

    let pipe = PipeBuilder()

    // ----- OTHER------------

    let map f x = bind x (return' << f)

    let apply f x = bind f (fun fe -> map fe x)

    let join x = bind x id

    let mapM mFunc sequ =
        let consF x ys = apply (map (listCons) (mFunc x)) ys
        Seq.foldBack (consF) sequ (return' [])
        |> map (Seq.ofList)

    let iterM mFunc sequ =
        mapM (mFunc) sequ
        |> map (ignore)

    let pipeTo p1 p2 = bindPull p2 (fun () -> p1)

    /// Execute an action repeatedly as long as the given boolean IO action returns true
    let iterWhileM (pAct : Pipeline<_,_,_,_,bool>) (act : Pipeline<_,_,_,_,'V>) =
        let rec whileMRec() =
            pipe { // check the predicate action
                let! p = pAct 
                match p with
                |true -> // unwrap the current action value then recurse
                    let! _ = act
                    return! whileMRec()
                |false -> return () // finished
            }
        whileMRec ()

    /// Yields the result of applying f until p holds.
    let rec iterateUntilM p f v =
        match p v with
        |true -> return' v
        |false ->  bind (f v) (iterateUntilM p f)

    /// Execute an action repeatedly until its result satisfies a predicate and return that result (discarding all others).
    let iterateUntil p x =  bind x (iterateUntilM p (const' x))

    /// Execute an action repeatedly until its result fails to satisfy a predicate and return that result (discarding all others).
    let iterateWhile p x = iterateUntil (not << p) x

    /// Calls the supplied pipeline forever
    let forever x = iterWhileM (return' true) x

/// Module to provide the definition of the io computation expression
[<AutoOpen>]
module PipeBuilders =
    let pipe = Pipeline.PipeBuilder()