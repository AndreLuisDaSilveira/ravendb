using System.Collections.Generic;
using System.Linq;
using FastTests;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using Raven.Client.Documents.Operations.Indexes;
using Xunit;

namespace SlowTests.MailingList
{
    public class RavenDB252 : RavenNewTestBase
    {
        [Fact]
        public void EntityNameIsNowCaseInsensitive()
        {
            using (var store = GetDocumentStore())
            {
                using (var commands = store.Commands())
                {
                    commands.Put("a", null, new
                    {
                        FirstName = "Oren"
                    }, new Dictionary<string, string>
                    {
                        {Constants.Documents.Metadata.Collection, "Users"}
                    });

                    commands.Put("b", null, new
                    {
                        FirstName = "Ayende"
                    }, new Dictionary<string, string>
                    {
                        {Constants.Documents.Metadata.Collection, "users"}
                    });
                }

                using (var session = store.OpenSession())
                {
                    Assert.NotEmpty(session.Query<User>().Where(x => x.FirstName == "Oren"));

                    Assert.NotEmpty(session.Query<User>().Where(x => x.FirstName == "Ayende"));
                }
            }
        }

        [Fact]
        public void EntityNameIsNowCaseInsensitive_Method()
        {
            using (var store = GetDocumentStore())
            {
                using (var commands = store.Commands())
                {
                    commands.Put("a", null, new
                    {
                        FirstName = "Oren"
                    }, new Dictionary<string, string>
                    {
                        {Constants.Documents.Metadata.Collection, "Users"}
                    });

                    commands.Put("b", null, new
                    {
                        FirstName = "Ayende"
                    }, new Dictionary<string, string>
                    {
                        {Constants.Documents.Metadata.Collection, "users"}
                    });
                }

                WaitForIndexing(store);

                store.Admin.Send(new PutIndexesOperation(new[] { new IndexDefinition
                {
                    Name = "UsersByName",
                    Maps = { "docs.users.Select(x=>new {x.FirstName })" }
                }}));

                WaitForIndexing(store);

                using (var session = store.OpenSession())
                {
                    Assert.NotEmpty(session.Query<User>("UsersByName").Customize(x => x.WaitForNonStaleResults()).Where(x => x.FirstName == "Oren"));

                    Assert.NotEmpty(session.Query<User>("UsersByName").Where(x => x.FirstName == "Ayende"));
                }
            }
        }

        private class User
        {
            public string Id { get; set; }

            public string FirstName { get; set; }
        }
    }
}
