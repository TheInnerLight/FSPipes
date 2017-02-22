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

module Pipes =
    /// Lift a value from the Async monad into the pipeline monad
    let liftAsync x = AsyncM <| async.Bind(x, (async.Return << Value))

    /// Await a value from a pipeline
    let await<'ain, 'bin, 'bout> : Pipeline<unit, 'ain, 'bin, 'bout, 'ain> = Pipeline.request() 

    /// Yield a supplied value to the pipeline
    let yield' v : Pipeline<'aout, 'ain, unit, 'bout, unit> = Pipeline.respond v

    /// Loops over a supplied pipeline replacing each yield using the supplied pipeline generation function
    let for' p f = Pipeline.bindResponse p f

    /// Creates a Producer of 'a from a sequence of 'a
    let each sequ  =
        Seq.fold (fun acc p -> Pipeline.bind acc (fun _ -> yield' p)) (Pipeline.return'()) sequ

    /// Runs an effect to generate a result within the IO monad
    let rec runEffect (effect : Effect<_>) =
        match effect with
        |Request (_)  -> invalidOp "Impossible"
        |Respond (_) -> invalidOp "Impossible"
        |AsyncM io -> async.Bind(io, runEffect)
        |Value r   -> async.Return r

    /// A set of convenience operators on pipelines
    module Operators =
        /// Monadic bind operator on pipelines
        let inline (>>=) x f = Pipeline.bind x f
        /// Map operator on pipelines
        let inline (<!>) f x = Pipeline.map f x
        /// Apply operator on pipelines
        let inline (<*>) f x = Pipeline.apply f x
        /// Sequence two pipelines, discarding the first argument
        let inline ( >>. ) u v = Pipeline.return' (const' id) <*> u <*> v
        /// Sequence two pipelines, discarding the value of the second argument.
        let inline ( .>> ) u v = Pipeline.return' const' <*> u <*> v
        /// Combined two pipelines, piping the output of the first to the input of the second
        let inline (>->) p1 p2 = Pipeline.pipeTo p1 p2

        let inline (+>>) p1 f = Pipeline.bindPull p1 f

        let inline (>+>) p1 p2 = fun x -> Pipeline.bindPull (p1 x) p2

        let inline (>>~) p1 f = Pipeline.bindPush (p1) f

        let inline (>~>) p1 p2 = fun x -> Pipeline.bindPush (p1 x) p2


    open Operators

    /// The identity pipe, it receives input from upstream and forwards it on downstream unchanged
    let identity<'a,'V> : Pipeline<unit, 'a, unit, 'a, 'V>  = Pipeline.pull()

    /// Creates a pipe that applies a function to every value flowing downstream
    let map f = for' identity (yield' << f)

    /// Creates a pipe that allows values to flow downstream if they satisfy a supplied predicate p
    let filter p = for' identity (fun a -> if (p a) then yield' a else Pipeline.return' ())

    /// Create a pipe that forwards a running aggregration of the data it has received so far downstream
    let scan f acc =
        let rec scanRec acc = 
            pipe {
                do! yield' acc // yield the running total
                let! x = await
                return! scanRec (f acc x) // recursive with new accumulator
            }
        scanRec acc

    /// Chunk accepts a chunking function that allows you to alter the size and format of any arrays flowing downstream
    let chunk chunker =
        let rec splitterRec leftover =
            pipe {
                let! x = await
                let x' = // create x' by appending any leftover data to x (if applicable)
                    match leftover with
                    |Some aLeft -> Array.append aLeft x
                    |None -> x
                // Handle the different chunking cases
                match chunker x' with
                |Choice1Of2 v -> // If the chunking function returns Choice1of2, all of the original data neatly chunks into the new format with nothing leftover
                    do! Pipeline.iterM (yield') v
                    return! splitterRec (None)
                |Choice2Of2 (v1, v2) -> // Here we retrieve some chunked data some data that is leftover in the original format
                    do! Pipeline.iterM (yield') v1
                    return! splitterRec (Some v2)
            }
        splitterRec None

    /// Decode a sequence of bytes to a string using the supplied encoding
    let decode enc =
        let decoder = (Encoding.createDotNetEncoding enc).GetDecoder()
        // split the input bytes into a choice of either completely converted into a string or converted partially with some bytes unconsumed
        let decodeChunker (bytes : byte[]) = 
            let chars = Array.zeroCreate<char> (Array.length bytes)
            let bytesUsed, charsUsed, _ = decoder.Convert(bytes, 0, Array.length bytes, chars, 0, Array.length chars, true)
            match bytesUsed = Array.length bytes with
            |true -> Choice1Of2 ([|System.String(Array.take charsUsed chars)|])
            |false -> Choice2Of2 ([|System.String(Array.take charsUsed chars)|], Array.skip bytesUsed bytes)
        // chunk with the above decoding chunker
        chunk decodeChunker

    /// Encode a sequence of strings into byte arrays using the supplied encoding
    let encode enc =
        let encoder = (Encoding.createDotNetEncoding enc)
        let getBytes (str: string) = encoder.GetBytes(str)
        for' identity (yield' << getBytes)

    /// Creates a pipe that applies a sequence producing function to every value flowing downstream, each value
    /// in the resulting sequence is yielded individually.
    let collect f = for' identity (each << f)

    /// Creates a pipe that forwards string values downstream line by line
    let lines<'V> : Pipeline<unit, string, unit, string, 'V> = collect (fun (str : string) -> str.Split('\n'))

    /// Creates a pipe that forwards string values downstream word by word
    let words<'V> : Pipeline<unit, string, unit, string, 'V>  = 
        collect (fun (str : string) -> 
            str.Split(' ') |> Seq.map (fun str -> str.Trim()))

    /// Creates a pipe that takes values while a condition is satisfied
    let takeWhile pred =
        let rec takeWhileRec() =
             pipe {
                let! x = await
                match pred x with
                |true -> // still taking values
                    do! yield' x
                    return! takeWhileRec()
                |false -> // finished
                    return ()
                
             }
        takeWhileRec()

    /// Creates a pipe that skips values while a condition is satisfied
    let skipWhile pred = 
        let rec skipWhileRec() =
             pipe {
                let! x = await
                match pred x with
                |true -> // still taking values
                    return! skipWhileRec()
                |false -> // finished
                    do! yield' x
                    return! identity
                
             }
        skipWhileRec()