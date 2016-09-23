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

type GroupedData<'T, 'Id> = 
    | Data of 'T 
    | Marker of 'Id

module Pipes =
    /// Lift a value from the IO monad into the pipeline monad
    let liftIO x = IOM <| IO.bind x (IO.return' << Value)

    /// Await a value from a pipeline
    let await<'ain, 'bin, 'bout> : Pipeline<unit, 'ain, 'bin, 'bout, 'ain> = Pipeline.request() 

    /// Yield a supplied value to the pipeline
    let yield' v : Pipeline<'aout, 'ain, unit, 'bout, unit> = Pipeline.respond v

    /// Loops over a supplied pipeline replacing each yield using the supplied pipeline generation function
    let for' p f = Pipeline.bindResponse p f

    /// Creates a Producer of 'a from a sequence of 'a
    let each sequ  =
        Seq.fold (fun acc p -> Pipeline.bind acc (fun _ -> yield' p)) (Pipeline.return'()) sequ

    /// Advances the producer getting a choice of either the final value or a value and the producer to generate the subsequent value
    let next (p : Producer<'bout, 'v>) : IO<Choice<'v, 'bout * Producer<'bout,'v>>> = 
        let rec nextRec p = 
            match p with
            |Request (_) -> invalidOp "Impossible"
            |Respond (a, fu) -> IO.return' (Choice2Of2 (a, fu ()))
            |IOM m  -> IO.bind m (nextRec)
            |Value r -> IO.return' (Choice1Of2 r)
        nextRec p

    /// Runs an effect to generate a result within the IO monad
    let rec runEffect (effect : Effect<_>) =
        match effect with
        |Request (_)  -> invalidOp "Impossible"
        |Respond (_) -> invalidOp "Impossible"
        |IOM io -> IO.bind io runEffect
        |Value r   -> IO.return' r

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

    /// Folds over a producer using a supplied accumulation function, initial accumulator value and producer.
    /// (Note: this function is not an idiomatic use of Pipes but it is included to permit the development of unit tests.)
    let fold f acc (p0 : Producer<_, _>) =
        let rec foldRec p x =
            match p with
            |Request (_)  -> invalidOp "Impossible"
            |Respond (a, fu) -> foldRec (fu ()) (f x a)
            |IOM m  -> IO.bind m (fun p' -> foldRec p' x)
            |Value _ -> IO.return' x
        foldRec p0 acc

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

    let encode2 enc arr =
        let encoder = (Encoding.createDotNetEncoding enc)
        let decoder = (Encoding.createDotNetEncoding enc).GetDecoder()
        let rec encode2Rec (leftover : byte[]) =
            pipe {
                let! (bytes : byte[]) = Pipeline.request leftover
                let chars = Array.zeroCreate<char> (Array.length bytes)
                let bytesUsed, charsUsed, _ = decoder.Convert(bytes, 0, Array.length bytes, chars, 0, Array.length chars, true)
                match bytesUsed = Array.length bytes with
                |true -> 
                    let! (resp : string) = Pipeline.respond <| System.String(Array.take charsUsed chars)
                    return! encode2Rec (encoder.GetBytes(resp))
                |false -> 
                    let! (resp : string) = Pipeline.respond <| System.String(Array.take charsUsed chars)
                    return! encode2Rec (Array.append (encoder.GetBytes(resp)) (Array.skip bytesUsed bytes))
            }
        encode2Rec arr

    let byteBuffer<'V> : Pipeline<_,_,_,_,'V> =
        let rec byteBufferRec leftover =
            pipe {
                let! (bytes : byte[]) = await
                let! (leftover' : byte[]) = Pipeline.respond (Array.append bytes leftover)
                return! byteBufferRec leftover'
            }
        byteBufferRec Array.empty

    /// Creates a pipe that applies a sequence producing function to every value flowing downstream, each value
    /// in the resulting sequence is yielded individually.
    let collect f = for' identity (fun a -> each (f a))

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

    module String =
        module private Internal =
            type Buffer = {Buff : string; Len : int}

            let push (str : string) buffer =
                let str' = buffer.Buff + str
                let start' = max (str'.Length - buffer.Len) 0
                let end' = min (str'.Length) (buffer.Len + start')
                {Buff = str'.Substring(start', end'-start'); Len = buffer.Len} 

        let groupAfter (sought : string) =
            let length = (sought.Length)
            let rec untilRec (buff : Internal.Buffer) =
                pipe {
                    let! (str : string) = await
                    let combined = buff.Buff + str
                    match combined.Contains(sought) with
                    |false -> 
                        do! yield' (Data str)
                        return! untilRec (Internal.push str buff)
                    |true -> 
                        do! yield' << Data <| combined.Substring(buff.Buff.Length, combined.IndexOf(sought) + length)
                        do! yield' <| Marker()
                        do! yield' << Data <| combined.Substring(combined.IndexOf(sought) + length + 1)
                        return! for' identity (yield' << Data)
                    }
            untilRec {Buff = System.String.Empty; Len = sought.Length}