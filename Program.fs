open Falco
open Falco.Routing
open Falco.HostBuilder
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

open System.Data.SQLite

[<Literal>]
let dbFileName = "database.db"

// let connectionString = sprintf "Data Source=%s;Version=3;" dbFileName
let connectionString = sprintf "Data Source=data/%s;Version=3;" dbFileName
type Message = { Id: int64; Content: string }

module Db =
    let initializeDatabase () =
        // Ensure the database file exists

        use connection = new SQLiteConnection(connectionString)
        connection.Open()

        // Check if the messages table exists
        use checkTableCmd = connection.CreateCommand()
        checkTableCmd.CommandText <- "SELECT name FROM sqlite_master WHERE type='table' AND name='messages';"
        let tableExists = checkTableCmd.ExecuteScalar() |> isNull |> not

        if not tableExists then
            // Create the messages table
            use createTableCmd = connection.CreateCommand()

            createTableCmd.CommandText <-
                """
                CREATE TABLE messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    content TEXT NOT NULL
                )
            """

            createTableCmd.ExecuteNonQuery() |> ignore
            printfn "Messages table created."
        else
            printfn "Messages table already exists."

    let getMessages () : Message array =
        use connection = new SQLiteConnection(connectionString)
        connection.Open()
        use command = new SQLiteCommand("SELECT id, content FROM messages", connection)
        use reader = command.ExecuteReader()

        [| while reader.Read() do
               yield
                   { Id = reader.GetInt64(0)
                     Content = reader.GetString(1) } |]

    let addMessage (content: string) : Message =
        use connection = new SQLiteConnection(connectionString)
        connection.Open()
        use command = new SQLiteCommand(connection)
        command.CommandText <- "INSERT INTO messages (content) VALUES (@content); SELECT last_insert_rowid();"
        command.Parameters.AddWithValue("@content", content) |> ignore
        let id = command.ExecuteScalar() :?> int64
        { Id = id; Content = content }

let getMessagesHandler: HttpHandler =
    fun ctx ->
        let messages = Db.getMessages ()
        Response.ofJson messages ctx


let x: HttpHandler =
    Response.withStatusCode 400
    >> Response.ofJson {| Error = "Content is required" |}

let addMessageHandler: HttpHandler =
    fun ctx ->
        task {
            let! form = Request.getForm ctx

            match form.TryGetString "content" with
            | Some content ->
                let message = Db.addMessage content
                printfn $"{content}"
                return! Response.ofJson message ctx
            | None ->
                return!
                    (Response.withStatusCode 400
                     >> Response.ofJson {| Error = "Content is required" |})
                        ctx

        }


let configureServices (services: IServiceCollection) = services.AddFalco() |> ignore

let configureApp (endpoints: HttpEndpoint list) (app: IApplicationBuilder) = app.UseFalco(endpoints) |> ignore


[<EntryPoint>]
let main _ =
    System.IO.File.WriteAllText("data/test.txt", "abc")
    printfn "starting"
    Db.initializeDatabase ()
    printfn "Database initialized."

    webHost [||] {
        endpoints
            [ get "/" (Response.ofPlainText "Hello World")
              get "/test" (Response.ofJson {| messge = "Test" |})
              get "/messages" getMessagesHandler
              post "/messages" addMessageHandler ]
    }

    0
