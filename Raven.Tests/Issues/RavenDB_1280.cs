﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Raven.Client;
using Raven.Client.Indexes;
using Xunit;

namespace Raven.Tests.Indexes
{
	public class RavenDB_1280 : RavenTest
	{
		[TimeBombedFact(2014,4,30, "Performance issue, Pawel investigating this")]
		public void Referenced_Docs_Are_Indexed_During_Heavy_Writing()
		{
			const int iterations = 6000;

			using (var documentStore = NewRemoteDocumentStore(requestedStorage:"esent"))
			{
				Trace.Write("Making parallel inserts...");
				Parallel.For(0, iterations, i =>
				{
// ReSharper disable once AccessToDisposedClosure
					using (var session = documentStore.OpenSession())
					{
						session.Store(new EmailDocument {Id = "Emails/"+ i,To = "root@localhost", From = "nobody@localhost", Subject = "Subject" + i});
						session.SaveChanges();
					}

// ReSharper disable once AccessToDisposedClosure
					using (var session = documentStore.OpenSession())
					{
						session.Store(new EmailText { Id = "Emails/" + i + "/text", Body = "MessageBody" + i });
						session.SaveChanges();
					}
				});
				
				new EmailIndex().Execute(documentStore);
				WaitForIndexing(documentStore,null,TimeSpan.FromMinutes(1));

				using (var session = documentStore.OpenSession())
				{
					var results = session.Query<EmailIndexDoc, EmailIndex>().Count(e => e.Body.StartsWith("MessageBody"));
				    try
				    {
                        Assert.Equal(iterations, results);
				    }
				    catch (Exception ex)
				    {
                        var missingDocs = session.Query<EmailIndexDoc, EmailIndex>().AsProjection<EmailIndexDoc>()
                                                                                    .Where(e => !e.Body.StartsWith("MessageBody"))
                                                                                    .ToList();
                        Console.WriteLine(string.Join(", ", missingDocs.Select(doc => doc.Id).ToArray()));
				        Console.WriteLine(ex.Message);
				        throw;
				    }
				}
			}
		}

     [TimeBombedFact(2014, 4, 30, "Performance issue, Pawel investigating this")]
        public void CanHandleMultipleMissingDocumentsInMultipleIndexes()
        {
            using (var store = NewDocumentStore())
            {
                var indexDefinition = new EmailIndex().CreateIndexDefinition();

                for (int i = 0; i < 4; i++)
                {
                    store.DatabaseCommands.PutIndex("email" + i, indexDefinition);
                    
                }

                using (var session = store.OpenSession())
                {
                    session.Store(entity: new EmailDocument { });
                    session.Store(entity: new EmailDocument { });
                    session.SaveChanges();
                }

                WaitForIndexing(store, timeout: TimeSpan.FromSeconds(10));
            }
        }

		public class EmailIndex : AbstractIndexCreationTask<EmailDocument, EmailIndexDoc>
		{
			public EmailIndex()
			{
				Map =
					emails => from email in emails
							let text = LoadDocument<EmailText>(email.Id + "/text") 				
							select new
									{
										email.To,
										email.From,
										email.Subject,
										Body = text == null ? null : text.Body
									};
			}
		}

		public class EmailDocument
		{
			public string Id { get; set; }
			public string To { get; set; }
			public string From { get; set; }
			public string Subject { get; set; }
		}

		public class EmailText
		{
			public string Id { get; set; }
			public string Body { get; set; }
		}

		public class EmailIndexDoc
		{
			public string Id { get; set; }
			public string To { get; set; }
			public string From { get; set; }
			public string Subject { get; set; }
			public string Body { get; set; }			
		}
	}
}
