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

namespace NovelFS.NovelIO.UnitTests

open NovelFS.NovelIO
open NovelFS.NovelIO.BinaryPickler
open NovelFS.FSPipes
open Pipes.Operators
open FsCheck
open FsCheck.Xunit

module PipeTestHelper =
    /// Effectfully transforms a pipe into a list
    let toList pipe =
        Pipes.fold (fun acc v ->acc @ [v]) [] pipe
        |> IO.run

type ``Pipes Unit Tests``() =
    [<Property>]
    static member ``identity pipe on pure sequence forwards values downstream unchanged`` (values : int list) =
        let ident = Pipes.each values >-> Pipes.identity
        let list = PipeTestHelper.toList ident
        list = values

    [<Property>]
    static member ``map on pure sequence transforms each element by mapping function`` (values : int list) =
        let mapped = Pipes.each values >-> Pipes.map ((+) 1)
        let list = PipeTestHelper.toList mapped
        list = List.map ((+) 1) values

    [<Property>]
    static member ``filter on pure sequence removes elements that do not satisfy predicate`` (values : int list) =
        let filtered = Pipes.each values >-> Pipes.filter (fun x -> x > 5)
        let list = PipeTestHelper.toList filtered
        list = List.filter (fun x -> x > 5) values

    [<Property>]
    static member ``takeWhile on pure sequence takes elements while predicate is satisfied`` (values : int list) =
        let took = Pipes.each values >-> Pipes.takeWhile (fun x -> x > 5)
        let list = PipeTestHelper.toList took
        list = List.takeWhile (fun x -> x > 5) values

    [<Property>]
    static member ``skipWhile on pure sequence skips elements while predicate is satisfied`` (values : int list) =
        let took = Pipes.each values >-> Pipes.skipWhile (fun x -> x > 5)
        let list = PipeTestHelper.toList took
        list = List.skipWhile (fun x -> x > 5) values

    [<Property>]
    static member ``scan (+) on pure string sequence concatenates strings`` (values : NonEmptyString list) =
        let values = values |> List.map (fun x -> x.Get)
        let took = Pipes.each values >-> Pipes.scan (+) System.String.Empty
        let list = PipeTestHelper.toList took
        list = List.scan ((+)) System.String.Empty values

    

