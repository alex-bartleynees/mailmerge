using System.Net;
using System.Net.Http.Json;
using ClosedXML.Excel;
using MailMerge.IntegrationTests.Infrastructure;

namespace MailMerge.IntegrationTests.Contacts;

/// <summary>Ports test_import_review_and_commit, test_edit_add_column_and_fix_email, test_export_roundtrip and
/// test_csv_import_and_bad_file from the Python app.</summary>
[Collection(ApiCollection.Name)]
public sealed class ImportAndEditTests(MailMergeApiFactory api)
{
    private static byte[] ClientsSheet() => TestData.Xlsx("Clients",
        ["First Name", "Email Address", "Company", "Suburb"],
        ["Ann", "ann@example.com", "Acme", "Hutt"],
        ["Bob", "BOB@example.com ", "Beta", ""],
        ["Cy", "not-an-email", "Gamma", "Nelson"],
        ["Dup", "ann@example.com", "Acme2", "X"],
        ["NoMail", "", "Delta", "Y"],
        ["Eve", "eve@example.com", "Echo", "Wellington"]);

    private static async Task<(HttpClient Client, long ListId)> ImportClients(MailMergeApiFactory api)
    {
        var client = api.CreateStaffClient();
        var preview = await (await client.PostAsync("/api/imports", TestData.File("clients.xlsx", ClientsSheet())))
            .ReadAsync<ImportPreviewDto>();
        var result = await client.PostAsync<CommitImportDto>($"/api/imports/{preview.Token}/commit", new
        {
            sheet = "Clients", emailColumn = "Email Address", newListName = "Clients", updateExisting = false,
        });
        return (client, result.ListId);
    }

    [Fact]
    public async Task Import_previews_then_commits_with_cleaned_addresses()
    {
        var client = api.CreateStaffClient();

        var preview = await (await client.PostAsync("/api/imports", TestData.File("clients.xlsx", ClientsSheet())))
            .ReadAsync<ImportPreviewDto>();

        preview.EmailColumn.Should().Be("Email Address");
        preview.Headers.Should().Equal("First Name", "Email Address", "Company", "Suburb");
        preview.Sheets.Should().Equal("Clients");
        preview.RowCount.Should().Be(6);
        preview.InvalidCount.Should().Be(1);
        preview.BlankCount.Should().Be(1);

        var result = await client.PostAsync<CommitImportDto>($"/api/imports/{preview.Token}/commit", new
        {
            sheet = "Clients", emailColumn = "Email Address", newListName = "Clients", updateExisting = false,
        });

        result.Added.Should().Be(4);
        result.Duplicates.Should().Be(1);
        result.NoEmail.Should().Be(1);
        result.Invalid.Should().Be(1);

        var list = await client.GetAsync<ListContactsDto>($"/api/lists/{result.ListId}/contacts");
        list.Columns.Should().Equal("Email", "First Name", "Company", "Suburb");
        var byEmail = list.Contacts.ToDictionary(c => c.Email);
        byEmail.Should().ContainKey("bob@example.com", "addresses are trimmed and lower-cased");
        byEmail["not-an-email"].Valid.Should().BeFalse("bad addresses are kept and flagged so they can be fixed");
        byEmail["ann@example.com"].Data["Company"].Should().Be("Acme", "the first row wins over the file's duplicate");

        (await client.GetAsync($"/api/imports/{preview.Token}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "an upload is single-use");
    }

    [Fact]
    public async Task Reimport_into_existing_list_with_update_fills_in_non_blank_values()
    {
        var (client, listId) = await ImportClients(api);
        var update = TestData.Xlsx("Sheet1",
            ["email", "Company", "Phone"],
            ["ann@example.com", "Acme Ltd", "021"],
            ["eve@example.com", "", "022"],
            ["new@example.com", "Newco", ""]);

        var preview = await (await client.PostAsync("/api/imports", TestData.File("update.xlsx", update)))
            .ReadAsync<ImportPreviewDto>();
        var result = await client.PostAsync<CommitImportDto>($"/api/imports/{preview.Token}/commit",
            new { emailColumn = "email", listId, updateExisting = true });

        result.Should().BeEquivalentTo(new { Added = 1, Updated = 2, Duplicates = 0 });
        var list = await client.GetAsync<ListContactsDto>($"/api/lists/{listId}/contacts");
        list.Columns.Should().EndWith("Phone");
        var eve = list.Contacts.Single(c => c.Email == "eve@example.com");
        eve.Data["Company"].Should().Be("Echo", "a blank cell doesn't erase existing data");
        eve.Data["Phone"].Should().Be("022");
        list.Contacts.Single(c => c.Email == "ann@example.com").Data["Company"].Should().Be("Acme Ltd");
    }

    [Fact]
    public async Task Edit_fixes_an_address_rejects_duplicates_and_flags_do_not_email()
    {
        var (client, listId) = await ImportClients(api);
        var contacts = (await client.GetAsync<ListContactsDto>($"/api/lists/{listId}/contacts")).Contacts;
        var cy = contacts.Single(c => c.Email == "not-an-email");

        var fixedCy = await client.PutAsync<ContactDto>($"/api/contacts/{cy.Id}",
            new { data = new Dictionary<string, string>(cy.Data) { ["Email"] = "cy@example.com" } });
        fixedCy.Valid.Should().BeTrue();

        var duplicate = await client.PutAsJsonAsync($"/api/contacts/{cy.Id}",
            new { data = new Dictionary<string, string>(cy.Data) { ["Email"] = "ann@example.com" } });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await client.PostAsJsonAsync($"/api/lists/{listId}/columns", new { name = "Nickname" })).StatusCode
            .Should().Be(HttpStatusCode.OK);
        (await client.PostAsJsonAsync($"/api/lists/{listId}/columns", new { name = "nickname" })).StatusCode
            .Should().Be(HttpStatusCode.Conflict);
        var badColumn = await client.PostAsJsonAsync($"/api/lists/{listId}/columns", new { name = "a|b" });
        badColumn.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badColumn.ProblemDetailAsync()).Should().Contain("{ } |");

        var fay = await client.PostAsJsonAsync($"/api/lists/{listId}/contacts",
            new { data = new Dictionary<string, string> { ["Email"] = "Fay@Example.com", ["First Name"] = "Fay <b>" } });
        fay.StatusCode.Should().Be(HttpStatusCode.Created);
        (await fay.ReadAsync<ContactDto>()).Email.Should().Be("fay@example.com");

        var eve = contacts.Single(c => c.Email == "eve@example.com");
        (await client.PutAsync<ContactDto>($"/api/contacts/{eve.Id}", new { doNotEmail = true })).DoNotEmail
            .Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_column_removes_its_values_but_email_stays()
    {
        var (client, listId) = await ImportClients(api);

        var removed = await client.DeleteAsync($"/api/lists/{listId}/columns/Suburb");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/lists/{listId}/columns/Email")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);

        var list = await client.GetAsync<ListContactsDto>($"/api/lists/{listId}/contacts");
        list.Columns.Should().NotContain("Suburb");
        list.Contacts.Should().OnlyContain(c => !c.Data.ContainsKey("Suburb"));
    }

    [Fact]
    public async Task Export_round_trips_to_xlsx()
    {
        var (client, listId) = await ImportClients(api);

        var response = await client.GetAsync($"/api/lists/{listId}/export");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileNameStar.Should().Be("Clients.xlsx");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes[..2].Should().Equal((byte)'P', (byte)'K');
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var header = workbook.Worksheet(1).Row(1).Cells(1, 3).Select(c => c.GetString());
        header.Should().Equal("Email", "First Name", "Company");
    }

    [Fact]
    public async Task Csv_import_and_bad_files()
    {
        var client = api.CreateStaffClient();

        var csv = await (await client.PostAsync("/api/imports", TestData.File("x.csv", "Name;E-mail\nZed;zed@example.com\n"u8.ToArray())))
            .ReadAsync<ImportPreviewDto>();
        csv.EmailColumn.Should().Be("E-mail");
        csv.Headers.Should().Equal("Name", "E-mail");

        var pdf = await client.PostAsync("/api/imports", TestData.File("x.pdf", "%PDF"u8.ToArray()));
        pdf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pdf.ProblemDetailAsync()).Should().Contain(".xlsx");

        var garbage = await client.PostAsync("/api/imports", TestData.File("x.xlsx", "garbage"u8.ToArray()));
        garbage.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await garbage.ProblemDetailAsync()).Should().StartWith("Couldn't open that file");
    }

    [Fact]
    public async Task Another_workspace_cannot_see_or_change_the_list()
    {
        var (_, listId) = await ImportClients(api);
        var other = api.CreateStaffClient();

        (await other.GetAsync<List<ListDto>>("/api/lists")).Should().NotContain(l => l.Id == listId);
        (await other.GetAsync($"/api/lists/{listId}/contacts")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/lists/{listId}/export")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/lists/{listId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
