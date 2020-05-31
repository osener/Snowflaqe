[<RequireQualifiedAccess>]
module Snowflaqe.CodeGen

open System
open System.Linq
open FsAst
open Fantomas
open Snowflaqe.Types
open FSharp.Compiler.SyntaxTree
open FSharp.Compiler.XmlDoc
open FSharp.Compiler.Range
open System.Collections.Generic

let compiledName (name: string) = SynAttribute.Create("CompiledName", name)

let capitalize (input: string) = input.First().ToString().ToUpper() + String.Join("", input.Skip(1))

let normalizeName (unionCase: string) =
    if not(unionCase.Contains "_") then
        capitalize unionCase
    else
        unionCase.Split [| '_'; '-' |]
        |> Array.filter String.isNotNullOrEmpty
        |> Array.map capitalize
        |> String.concat ""

let capitalizeEnum (input: string) =
    input.First().ToString().ToUpper() + String.Join("", input.Skip(1)).ToLowerInvariant()
let normalizeEnumName (unionCase: string) =
    if not(unionCase.Contains "_") then
        capitalizeEnum unionCase
    else
        unionCase.Split [| '_'; '-' |]
        |> Array.filter String.isNotNullOrEmpty
        |> Array.map capitalizeEnum
        |> String.concat ""

type SynAttribute with
    static member Create(idents: string list) : SynAttribute =
        {
           AppliesToGetterAndSetter = false
           ArgExpr = SynExpr.Const (SynConst.Unit, range0)
           Range = range0
           Target = None
           TypeName = LongIdentWithDots(List.map Ident.Create idents, [ ])
        }

let createEnumType (enumType: GraphqlEnum) =
    let info : SynComponentInfoRcd = {
        Access = None
        Attributes = [
            SynAttributeList.Create [
                SynAttribute.Create [ "Fable"; "Core"; "StringEnum" ]
                SynAttribute.RequireQualifiedAccess()
            ]
        ]

        Id = [ Ident.Create enumType.name ]
        XmlDoc = PreXmlDoc.Create enumType.description
        Parameters = [ ]
        Constraints = [ ]
        PreferPostfix = false
        Range = range0
    }

    let values = enumType.values |> List.filter (fun enumValue -> not enumValue.deprecated)

    let enumRepresentation = SynTypeDefnSimpleReprUnionRcd.Create([
        for value in values ->
            let attrs = [ SynAttributeList.Create(compiledName value.name) ]
            let docs = PreXmlDoc.Create value.description
            SynUnionCase.UnionCase(attrs, Ident.Create (normalizeEnumName value.name), SynUnionCaseType.UnionCaseFields [], docs, None, range0)
    ])

    let simpleType = SynTypeDefnSimpleReprRcd.Union(enumRepresentation)
    SynModuleDecl.CreateSimpleType(info, simpleType)

let optionOfSystemDot id inner =
    SynFieldRcd.CreateApp id (LongIdentWithDots.Create [ "Option" ]) [ (LongIdentWithDots.Create [ "System"; inner ]) ]

let listOfSystemDot id inner =
    SynFieldRcd.CreateApp id (LongIdentWithDots.Create [ "list" ]) [ (LongIdentWithDots.Create [ "System"; inner ]) ]

let systemDot id inner =
    SynFieldRcd.Create(id, LongIdentWithDots([ Ident.Create "System"; Ident.Create inner ], []))


type SynType with
    static member Create(name: string) = SynType.CreateLongIdent name

    static member Option(inner) =
        SynType.App(
            typeName=SynType.CreateLongIdent "Option",
            typeArgs=[ inner ],
            commaRanges = [ ],
            isPostfix = false,
            range=range0,
            greaterRange=None,
            lessRange=None
        )

    static member Option(inner: string) =
        SynType.App(
            typeName=SynType.CreateLongIdent "Option",
            typeArgs=[ SynType.Create inner ],
            commaRanges = [ ],
            isPostfix = false,
            range=range0,
            greaterRange=None,
            lessRange=None
        )

    static member List(inner) =
        SynType.App(
            typeName=SynType.CreateLongIdent "list",
            typeArgs=[ inner ],
            commaRanges = [ ],
            isPostfix = false,
            range=range0,
            greaterRange=None,
            lessRange=None
        )

    static member List(inner: string) =
        SynType.App(
            typeName=SynType.CreateLongIdent "list",
            typeArgs=[ SynType.Create inner ],
            commaRanges = [ ],
            isPostfix = false,
            range=range0,
            greaterRange=None,
            lessRange=None
        )

    static member DateTimeOffset() =
        SynType.LongIdent(LongIdentWithDots.Create [ "System"; "DateTimeOffset" ])

    static member DateTime() =
        SynType.LongIdent(LongIdentWithDots.Create [ "System"; "DateTime" ])

    static member Int() =
        SynType.Create "int"

    static member String() =
        SynType.Create "string"

    static member Bool() =
        SynType.Create "bool"

    static member Float() =
        SynType.Create "float"

    static member Decimal() =
        SynType.Create "decimal"

type SynFieldRcd with
    static member Create(name: string, fieldType: SynType) =
        {
            Access = None
            Attributes = [ ]
            Id = Some (Ident.Create name)
            IsMutable = false
            IsStatic = false
            Range = range0
            Type = fieldType
            XmlDoc= PreXmlDoc.Empty
        }

    static member Create(name: string, fieldType: string) =
        {
            Access = None
            Attributes = [ ]
            Id = Some (Ident.Create name)
            IsMutable = false
            IsStatic = false
            Range = range0
            Type = SynType.Create fieldType
            XmlDoc= PreXmlDoc.Empty
        }

let rec createFSharpType (name: string option) (graphqlType: GraphqlFieldType) =
    match graphqlType with
    | GraphqlFieldType.NonNull(GraphqlFieldType.Scalar scalar) ->
        match scalar with
        | GraphqlScalar.Int -> SynType.Int()
        | GraphqlScalar.String -> SynType.String()
        | GraphqlScalar.Boolean -> SynType.Bool()
        | GraphqlScalar.Float -> SynType.Float()
        | GraphqlScalar.ID -> SynType.String()
        | GraphqlScalar.Custom "Decimal" -> SynType.Decimal()
        | GraphqlScalar.Custom "DateTimeOffset" -> SynType.DateTimeOffset()
        | GraphqlScalar.Custom "DateTime" -> SynType.DateTime()
        | GraphqlScalar.Custom custom -> SynType.Create custom

    | GraphqlFieldType.NonNull(GraphqlFieldType.List innerType) ->
        let innerFSharpType = createFSharpType name innerType
        SynType.List(innerFSharpType)

    | GraphqlFieldType.NonNull(GraphqlFieldType.EnumRef enumType) ->
        SynType.Create enumType

    | GraphqlFieldType.NonNull(GraphqlFieldType.InputObjectRef objectRef) ->
        SynType.Create objectRef

    | GraphqlFieldType.NonNull(GraphqlFieldType.ObjectRef objectRef) ->
        SynType.Create (Option.defaultValue objectRef name)

    | GraphqlFieldType.Scalar scalar ->
        let innerFSharpType =
            match scalar with
            | GraphqlScalar.Int -> SynType.Int()
            | GraphqlScalar.String -> SynType.String()
            | GraphqlScalar.Boolean -> SynType.Bool()
            | GraphqlScalar.Float -> SynType.Float()
            | GraphqlScalar.ID -> SynType.String()
            | GraphqlScalar.Custom "Decimal" -> SynType.Decimal()
            | GraphqlScalar.Custom "DateTimeOffset" -> SynType.DateTimeOffset()
            | GraphqlScalar.Custom "DateTime" -> SynType.DateTime()
            | GraphqlScalar.Custom custom -> SynType.Create custom

        SynType.Option(innerFSharpType)

    | GraphqlFieldType.List innerType ->
        let innerFSharpType = createFSharpType name innerType
        SynType.Option(SynType.List(innerFSharpType))

    | GraphqlFieldType.EnumRef enumType ->
        SynType.Option(SynType.Create enumType)

    | GraphqlFieldType.InputObjectRef objectRef ->
        SynType.Option(SynType.Create objectRef)

    | GraphqlFieldType.ObjectRef objectRef ->
        SynType.Option(SynType.Create (Option.defaultValue objectRef name))

    | GraphqlFieldType.NonNull(inner) ->
        createFSharpType name inner

let createInputRecord (input: GraphqlInputObject) =
    let info : SynComponentInfoRcd = {
        Access = None
        Attributes = [ ]
        Id = [ Ident.Create input.name ]
        XmlDoc = PreXmlDoc.Create input.description
        Parameters = [ ]
        Constraints = [ ]
        PreferPostfix = false
        Range = range0
    }

    let fields = input.fields |> List.filter (fun field -> not field.deprecated)

    let recordRepresentation = SynTypeDefnSimpleReprRecordRcd.Create [
        for field in fields ->
            let recordFieldType = createFSharpType None field.fieldType
            let recordField = SynFieldRcd.Create(field.fieldName, recordFieldType)
            { recordField with XmlDoc = PreXmlDoc.Create field.description }
    ]

    let simpleType = SynTypeDefnSimpleReprRcd.Record recordRepresentation

    SynModuleDecl.CreateSimpleType(info, simpleType)


let createGlobalTypes (schema: GraphqlSchema) =
    let enums =
        schema.types
        |> List.choose (function
            | GraphqlType.Enum enumType when not (enumType.name.StartsWith "__")  -> Some enumType
            | _ -> None)
        |> List.map createEnumType

    let inputs =
        schema.types
        |> List.choose (function
            | GraphqlType.InputObject objectDefn when not (objectDefn.name.StartsWith "__") -> Some objectDefn
            | _ -> None)
        |> List.map createInputRecord

    List.append enums inputs


let nextTick (name: string) (visited: ResizeArray<string>) =
    if not (visited.Contains name) then
        name
    else
    visited
    |> Seq.toList
    |> List.filter (fun visitedName -> visitedName.StartsWith name)
    |> List.map (fun visitedName -> visitedName.Replace(name, ""))
    |> List.choose(fun rest ->
        match Int32.TryParse rest with
        | true, n -> Some n
        | _ -> None)
    |> function
        | [ ] -> name + "1"
        | ns -> name + (string (List.max ns + 1))

let findNextTypeName fieldName objectName (selections: string list) (visitedTypes: ResizeArray<string>) =
    let nestedSelectionType =
        selections
        |> List.map normalizeName
        |> String.concat "And"

    if not (visitedTypes.Contains objectName) then
        objectName
    elif not (visitedTypes.Contains (normalizeName fieldName)) then
        normalizeName fieldName
    elif not (visitedTypes.Contains nestedSelectionType) && selections.Length <= 3 && selections.Length < 1 then
        nestedSelectionType
    elif not (visitedTypes.Contains (normalizeName fieldName + "From" + objectName)) then
        objectName + normalizeName fieldName
    else
        nextTick (normalizeName fieldName + "From" + objectName) visitedTypes

let rec extractTypeName = function
    | GraphqlFieldType.Scalar scalar ->
        match scalar with
        | GraphqlScalar.Int -> "Int"
        | GraphqlScalar.Boolean -> "Boolean"
        | GraphqlScalar.String -> "String"
        | GraphqlScalar.Float -> "Float"
        | GraphqlScalar.ID -> "ID"
        | GraphqlScalar.Custom custom -> custom

    | GraphqlFieldType.ObjectRef objectRef -> objectRef
    | GraphqlFieldType.EnumRef enumRef -> enumRef
    | GraphqlFieldType.InputObjectRef objectRef -> objectRef

    | GraphqlFieldType.NonNull fieldType ->
        extractTypeName fieldType

    | GraphqlFieldType.List fieldType ->
        extractTypeName fieldType

let rec generateFields (typeName: string) (description: string option) (selections: SelectionSet) (schemaType: GraphqlObject) (schema: GraphqlSchema) (visitedTypes: ResizeArray<string>) (types: Dictionary<string,SynModuleDecl>)  =
    let info : SynComponentInfoRcd = {
        Access = None
        Attributes = [ ]
        Id = [ Ident.Create typeName ]
        XmlDoc = PreXmlDoc.Create description
        Parameters = [ ]
        Constraints = [ ]
        PreferPostfix = false
        Range = range0
    }

    let selectedFields =
        selections.nodes
        |> List.choose (function
            | GraphqlNode.Field field -> Some field
            | _ -> None)

    let recordRepresentation = SynTypeDefnSimpleReprRecordRcd.Create [
        for field in selectedFields do
            let fieldTypeInfo =
                schemaType.fields
                |> List.tryFind (fun fieldType' -> fieldType'.fieldName = field.name)

            match fieldTypeInfo with
            | None ->
                ()
            | Some fieldInfo when Query.fieldCanExpand fieldInfo.fieldType ->
                let fieldName = field.alias |> Option.defaultValue field.name
                let objectName = extractTypeName fieldInfo.fieldType
                let nestedFieldType =
                    schema.types
                    |> List.tryPick (function
                        | GraphqlType.Object objectDef when objectDef.name = objectName -> Some objectDef
                        | _ -> None)
                match nestedFieldType, field.selectionSet with
                | Some objectDef, Some nestedSelectionSet ->
                    let nestedFields =
                        nestedSelectionSet.nodes
                        |> List.choose (function
                            | GraphqlNode.Field field -> field.alias |> Option.defaultValue field.name |> Some
                            | _ -> None)

                    let typeName = findNextTypeName fieldName objectName nestedFields visitedTypes

                    visitedTypes.Add(typeName)
                    let nestedType = generateFields typeName fieldInfo.description nestedSelectionSet objectDef schema visitedTypes types
                    types.Add(typeName, nestedType)
                    SynFieldRcd.Create(fieldName, createFSharpType (Some typeName) fieldInfo.fieldType)
                | _ ->
                    ()

            | Some fieldInfo ->
                let fieldName = field.alias |> Option.defaultValue field.name
                let recordFieldType = createFSharpType None fieldInfo.fieldType
                let recordField = SynFieldRcd.Create(fieldName, recordFieldType)
                { recordField with XmlDoc = PreXmlDoc.Create fieldInfo.description }
    ]

    let simpleType = SynTypeDefnSimpleReprRcd.Record recordRepresentation
    SynModuleDecl.CreateSimpleType(info, simpleType)

let generateTypes (rootQueryName: string) (document: GraphqlDocument) (schema: GraphqlSchema) : SynModuleDecl list =
    match Query.findOperation (Query.expandDocumentFragments document) with
    | None -> [ ]
    | Some (GraphqlOperation.Query query) ->
        match Schema.findQuery schema with
        | None -> [ ]
        | Some queryType ->
            let visitedTypes = ResizeArray<string>()
            let allTypes = Dictionary<string, SynModuleDecl>()
            let rootType = generateFields rootQueryName queryType.description query.selectionSet queryType schema visitedTypes allTypes
            [
                for typeName in allTypes.Keys do
                    yield allTypes.[typeName]

                yield rootType
            ]

    | Some (GraphqlOperation.Mutation mutation) ->
        match Schema.findMutation schema with
        | None -> [ ]
        | Some mutationType ->
            let visitedTypes = ResizeArray<string>()
            let allTypes = Dictionary<string, SynModuleDecl>()
            let rootType = generateFields rootQueryName mutationType.description mutation.selectionSet mutationType schema visitedTypes allTypes
            [
                for typeName in allTypes.Keys do
                    yield allTypes.[typeName]

                yield rootType
            ]

let createNamespace name declarations =
    let xmlDoc = PreXmlDoc.Create [ ]
    SynModuleOrNamespace.SynModuleOrNamespace([ Ident.Create name ], true, SynModuleOrNamespaceKind.DeclaredNamespace,declarations,  xmlDoc, [ ], None, range.Zero)

let createQualifiedModule idens declarations =
    let xmlDoc = PreXmlDoc.Create [ ]
    SynModuleOrNamespace.SynModuleOrNamespace(idens |> List.map Ident.Create, true, SynModuleOrNamespaceKind.NamedModule,declarations,  xmlDoc, [ SynAttributeList.Create [ SynAttribute.RequireQualifiedAccess()  ]  ], None, range.Zero)

let createFile fileName modules =
    let qualfiedNameOfFile = QualifiedNameOfFile.QualifiedNameOfFile(Ident.Create fileName)
    ParsedImplFileInput.ParsedImplFileInput(fileName, false, qualfiedNameOfFile, [], [], modules, (false, false))

let formatAst file =
    formatAst (ParsedInput.ImplFile file)
    |> Async.RunSynchronously