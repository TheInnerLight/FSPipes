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

module Pipes =
    let rec lift x = IOM <| IO.bind x (fun r -> IO.return' (Value r))

    let await<'a> : Consumer<'a, 'a> = Pipeline.request() 

    let yield' v : Producer<'a, unit> = Pipeline.respond v

    let for' p f = Pipeline.bindResponse p f

    let each sequ : Producer<'a, unit> =
        Seq.fold (fun acc p -> Pipeline.bind acc (fun _ -> yield' p)) (Pipeline.return'()) sequ

    let next (p : Producer<'bout, 'v>) : IO<Choice<'v, 'bout * Producer<'bout,'v>>> = 
        let rec nextRec p = 
            match p with
            |Request (_, _) -> invalidOp "Impossible"
            |Respond (a, fu) -> IO.return' (Choice2Of2 (a, fu ()))
            |IOM m  -> IO.bind m (nextRec)
            |Value r -> IO.return' (Choice1Of2 r)
        nextRec p

    let rec runEffect (effect : Effect<_>) =
        match effect with
        |Request (_, _)  -> invalidOp "Impossible"
        |Respond (_, _) -> invalidOp "Impossible"
        |IOM io -> IO.bind io runEffect
        |Value r   -> IO.return' r

    module Operators =
        let (>>=) x f = Pipeline.bind x f

        let (>->) p1 p2 = Pipeline.pipe p1 p2

    type PipeBuilder() =
        member this.Return x = Pipeline.return' x
        member this.ReturnFrom x : Pipeline<_,_,_,_,_> = x
        member this.Bind (x, f) = Pipeline.bind x f

    let pipe = PipeBuilder()

    let chunk chunker =
        let rec splitterRec leftover =
            pipe {
                let! x = Pipeline.request()
                let x' = 
                    match leftover with
                    |Some aLeft -> Array.append aLeft x
                    |None -> x
                match chunker x' with
                |Choice1Of2 v -> 
                    do! Pipeline.respond v
                    return! splitterRec (None)
                |Choice2Of2 (v1, v2) ->
                    do! Pipeline.respond v1
                    return! splitterRec (Some v2)
            }
        splitterRec None

    let decode enc =
        let decoder = (Encoding.createDotNetEncoding enc).GetDecoder()
        let decodeChunker (bytes : byte[]) = 
            let chars = Array.zeroCreate<char> (Array.length bytes)
            let bytesUsed, charsUsed, finished = decoder.Convert(bytes, 0, Array.length bytes, chars, 0, Array.length chars, true)
            match bytesUsed = Array.length bytes with
            |true -> Choice1Of2 (System.String(Array.take charsUsed chars))
            |false -> Choice2Of2 (System.String(Array.take charsUsed chars), Array.skip bytesUsed bytes)
        chunk decodeChunker


