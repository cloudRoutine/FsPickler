﻿namespace Nessos.FsPickler

    open System
    open System.Collections.Generic
    open System.Globalization
    open System.IO
    open System.Numerics
    open System.Text

    open Nessos.FsPickler.Utils

    open Newtonsoft.Json

    type JsonPickleReader internal (textReader : TextReader, omitHeader, leaveOpen) =

        let jsonReader = new JsonTextReader(textReader) :> JsonReader
        do
            jsonReader.CloseInput <- not leaveOpen
//            jsonReader.SupportMultipleContent <- true

//        let mutable currentValueIsNull = false

        let mutable depth = 0
        let arrayStack = new Stack<int> ()
        do arrayStack.Push Int32.MinValue

        // do not write tag if omitting header or array element
        let omitTag () = (omitHeader && depth = 0) || arrayStack.Peek() = depth - 1

        interface IPickleFormatReader with
            
            member __.BeginReadRoot (tag : string) =
                do jsonReader.MoveNext()
                    
                if omitHeader then () else

                if jsonReader.TokenType <> JsonToken.StartObject then raise <| new InvalidDataException("invalid json root object.")
                else
                    do jsonReader.MoveNext()
                    let version = jsonReader.ReadPrimitiveAs<string> false "FsPickler"
                    if version <> AssemblyVersionInformation.Version then
                        raise <| new InvalidDataException(sprintf "Invalid FsPickler version %s." version)

                    let sTag = jsonReader.ReadPrimitiveAs<string> false "type"
                    if tag <> sTag then
                        raise <| new InvalidPickleTypeException(tag, sTag)

            member __.EndReadRoot () = 
                if not omitHeader then jsonReader.Read() |> ignore

            member __.BeginReadObject (tag : string) =
                
                if not <| omitTag () then
                    jsonReader.ReadProperty tag
                    jsonReader.MoveNext ()

                match jsonReader.TokenType with
                | JsonToken.Null ->
//                    jsonReader.Read() |> ignore
                    ObjectFlags.IsNull

                | JsonToken.StartArray ->
                    jsonReader.MoveNext()
                    arrayStack.Push depth
                    depth <- depth + 1
                    ObjectFlags.IsSequenceHeader

                | JsonToken.StartObject ->
                    do jsonReader.MoveNext()
                    depth <- depth + 1

                    if jsonReader.ValueAs<string> () = "pickle flags" then
                        jsonReader.MoveNext()
                        let csvFlags = jsonReader.ValueAs<string>()
                        jsonReader.MoveNext()
                        parseFlagCsv csvFlags
                    else
                        ObjectFlags.None

                | _ -> invalidJsonFormat ()

            member __.EndReadObject () =
                match jsonReader.TokenType with
                | JsonToken.Null -> ()
                | JsonToken.EndObject -> depth <- depth - 1
                | JsonToken.EndArray ->
                    arrayStack.Pop() |> ignore
                    depth <- depth - 1

                | _ -> invalidJsonFormat ()

                if omitHeader && depth = 0 then ()
                else jsonReader.MoveNext()

            member __.PreferLengthPrefixInSequences = false
            member __.ReadNextSequenceElement () = jsonReader.TokenType <> JsonToken.EndArray

//            member __.BeginReadBoundedSequence tag =
//                arrayStack.Push depth
//                depth <- depth + 1
//
//                let length = jsonReader.ReadPrimitiveAs<int64> false "length"
//                jsonReader.ReadProperty tag
//                jsonReader.MoveNext()
//
//                if jsonReader.TokenType = JsonToken.StartArray then
//                    jsonReader.MoveNext()
//                    int length
//                else
//                    raise <| new InvalidDataException("expected json array.")
//
//            member __.EndReadBoundedSequence () =
//                if jsonReader.TokenType = JsonToken.EndArray && jsonReader.Read () then
//                    arrayStack.Pop () |> ignore
//                    depth <- depth - 1
//                else
//                    raise <| InvalidDataException("expected end of array.")
//
//            member __.BeginReadUnBoundedSequence tag =
//                if not <| omitTag () then
//                    jsonReader.ReadProperty tag
//                    jsonReader.MoveNext()
//
//                arrayStack.Push depth
//                depth <- depth + 1
//
//                if jsonReader.TokenType = JsonToken.StartArray then
//                    jsonReader.MoveNext()
//                else
//                    raise <| new InvalidDataException("expected json array.")
//


            member __.ReadBoolean tag = jsonReader.ReadPrimitiveAs<bool> (omitTag ()) tag

            member __.ReadByte tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> byte
            member __.ReadSByte tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> sbyte

            member __.ReadInt16 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> int16
            member __.ReadInt32 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> int
            member __.ReadInt64 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag

            member __.ReadUInt16 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> uint16
            member __.ReadUInt32 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> uint32
            member __.ReadUInt64 tag = jsonReader.ReadPrimitiveAs<int64> (omitTag ()) tag |> uint64

            member __.ReadSingle tag =
                if not <| omitTag () then
                    jsonReader.ReadProperty tag
                    jsonReader.MoveNext()

                let value =
                    match jsonReader.TokenType with
                    | JsonToken.Float -> jsonReader.ValueAs<double> () |> single
                    | JsonToken.String -> Single.Parse(jsonReader.ValueAs<string>(), CultureInfo.InvariantCulture)
                    | _ -> raise <| new InvalidDataException("not a float.")

                jsonReader.Read() |> ignore
                value
                
            member __.ReadDouble tag =
                if not <| omitTag () then
                    jsonReader.ReadProperty tag
                    jsonReader.MoveNext()

                let value =
                    match jsonReader.TokenType with
                    | JsonToken.Float -> jsonReader.ValueAs<double> ()
                    | JsonToken.String -> Double.Parse(jsonReader.ValueAs<string>(), CultureInfo.InvariantCulture)
                    | _ -> raise <| new InvalidDataException("not a float.")

                jsonReader.Read() |> ignore
                value

            member __.ReadChar tag = let value = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag in value.[0]
            member __.ReadString tag = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag
            member __.ReadBigInteger tag = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag |> BigInteger.Parse

            member __.ReadGuid tag = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag |> Guid.Parse
            member __.ReadTimeSpan tag = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag |> TimeSpan.Parse
            member __.ReadDate tag = jsonReader.ReadPrimitiveAs<DateTime> (omitTag ()) tag

            member __.ReadDecimal tag = jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag |> decimal

            member __.ReadBytes tag = 
                match jsonReader.ReadPrimitiveAs<string> (omitTag ()) tag with
                | null -> null
                | value -> Convert.FromBase64String value

            member __.IsPrimitiveArraySerializationSupported = false
            member __.ReadPrimitiveArray _ _ = raise <| new NotImplementedException()

            member __.Dispose () = (jsonReader :> IDisposable).Dispose()