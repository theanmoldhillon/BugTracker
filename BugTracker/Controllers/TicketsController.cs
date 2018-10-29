using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using BugTracker.Helper;
using BugTracker.Models;
using BugTracker.Models.Classes;
using Microsoft.AspNet.Identity;
using System.IO;
using System.Web.Configuration;
using System.Net.Mail;

namespace BugTracker.Controllers
{
    [Authorize]
    public class TicketsController : Controller
    {
        private ApplicationDbContext db { get; set; }
        private UserRoleHelper UserRoleHelper { get; set; }
        public TicketsController()
        {
            db = new ApplicationDbContext();
            UserRoleHelper = new UserRoleHelper();
        }

        // GET: Tickets
        public ActionResult Index(string id)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                return View(db.Tickets.Include(t => t.TicketPriority).Include(t => t.Project).Include(t => t.TicketStatus).Include(t => t.TicketType).Where(p => p.CreatorId == User.Identity.GetUserId()).ToList());
            }
            return View(db.Tickets.Include(t => t.TicketPriority).Include(t => t.Project).Include(t => t.TicketStatus).Include(t => t.TicketType).ToList());
        }

        //Get UserTickets
        public ActionResult UserTickets()
        {
            string userID = User.Identity.GetUserId();
            if (User.IsInRole("Submitter"))
            {
                var tickets = db.Tickets.Where(t => t.CreatorId == userID).Include(t => t.Creator).Include(t => t.Assignee).Include(t => t.Project);
                return View("Index", tickets.ToList());
            }
            if (User.IsInRole("Developer"))
            {
                var tickets = db.Tickets.Where(t => t.AssigneeId == userID).Include(t => t.Comments).Include(t => t.Creator).Include(t => t.Assignee).Include(t => t.Project);
                return View("Index", tickets.ToList());
            }
            if (User.IsInRole("Project Manager"))
            {
                return View(db.Tickets.Include(t => t.TicketPriority).Include(t => t.Project).Include(t => t.Comments).Include(t => t.TicketStatus).Include(t => t.TicketType).Where(p => p.AssigneeId == userID).ToList());
            }
            return View("Index");
        }
        // Project Manger and Developer Tickets
        [Authorize(Roles = "Developer,Project Manager")]
        public ActionResult ProjectManagerOrDeveloperTickets()
        {
            string userId = User.Identity.GetUserId();
            var ProjectMangerOrDeveloperId = db.Users.Where(p => p.Id == userId).FirstOrDefault();
            var ProjectId = ProjectMangerOrDeveloperId.Projects.Select(p => p.Id).FirstOrDefault();
            var tickets = db.Tickets.Where(p => p.Id == ProjectId).ToList();
            return View("Index", tickets);
        }


        public ActionResult AssignDeveloper(int Id)
        {
            var model = new AssignDevelopersTicketModel();
            var ticket = db.Tickets.FirstOrDefault(p => p.Id == Id);
            var userRoleHelper = new UserRoleHelper();
            var users = userRoleHelper.UsersInRole("Developer");
            model.TicketId = Id;
            model.DeveloperList = new SelectList(users, "Id", "Name");
            return View(model);
        }
        [HttpPost]
        public ActionResult AssignDeveloper(AssignDevelopersTicketModel model)
        {
            var ticket = db.Tickets.FirstOrDefault(p => p.Id == model.TicketId);
            ticket.AssigneeId = model.SelectedDeveloperId;
            var userId = db.Users.FirstOrDefault(c => c.Id == model.SelectedDeveloperId);
            var personalEmailService = new PersonalEmailService();
            var mailMessage = new MailMessage(
               WebConfigurationManager.AppSettings["emailto"],
               userId.Email
               );
            mailMessage.Body = "Hello";
            mailMessage.Subject = "New Developer Assigned";
            mailMessage.IsBodyHtml = true;
            personalEmailService.Send(mailMessage);
            db.SaveChanges();
            return RedirectToAction("Index");
        }

        // GET: Tickets/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Tickets tickets = db.Tickets.Find(id);
            if (tickets == null)
            {
                return HttpNotFound();
            }
            return View(tickets);
        }

        [HttpPost]
        public ActionResult CreateComment(int id, string body)
        {
            var tickets = db.Tickets
               .Where(p => p.Id == id)
               .FirstOrDefault();
            var users = User.Identity.GetUserId();
            var userId = db.Users.FirstOrDefault(p => p.Id == users);
            var thisProject = userId.Projects.Select(p => p.Id);
            var thisticket = db.Tickets.Where(p => thisProject.Contains(p.ProjectId)).ToList();

            if (tickets == null)
            {
                return HttpNotFound();
            }
            if (string.IsNullOrWhiteSpace(body))
            {
                ViewBag.ErrorMessage = "Comment is required";
                return View("Details", tickets);
            }
            if (User.IsInRole("Admin") || (User.IsInRole("Project Manager") && thisticket.Any(p => p.Id == id)) || (User.IsInRole("Submitter") && tickets.CreatorId == users) || (User.IsInRole("Developer") && tickets.AssigneeId == users))
            {
                var comment = new TicketComment();
                comment.UserId = User.Identity.GetUserId();
                comment.TicketId = tickets.Id;
                comment.Created = DateTime.Now;
                comment.Comment = body;
                db.TicketComments.Add(comment);
                var personalEmailService = new PersonalEmailService();
                var mailMessage = new MailMessage(
                   WebConfigurationManager.AppSettings["emailto"],
                   userId.Email
                   );
                mailMessage.Body = "New Comment";
                mailMessage.Subject = "New Comment";
                mailMessage.IsBodyHtml = true;
                personalEmailService.Send(mailMessage);
                db.SaveChanges();
            }
            return RedirectToAction("Details", new {id });
        }

        // GET: Tickets/Create
        [Authorize(Roles = "Submitter")]
        public ActionResult Create()
        {
            ViewBag.AssigneeId = new SelectList(db.Users, "Id", "DisplayName");
            ViewBag.CreatorId = new SelectList(db.Users, "Id", "DisplayName");
            ViewBag.ProjectId = new SelectList(db.Projects, "Id", "Name");
            ViewBag.TicketPriorityId = new SelectList(db.TicketPriorities, "Id", "Name");
            ViewBag.TicketTypeId = new SelectList(db.TicketTypes, "Id", "Name");
            return View();
        }

        // POST: Tickets/Create
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        //[Authorize(Roles = "Submitter")]
        public ActionResult CreateAttachment(int Id, [Bind(Include = "Id,Description,TicketTypeId")] TicketAttachment ticketAttachment, HttpPostedFileBase image)
        {
            if (ModelState.IsValid)
            {
                var tickets = db.Tickets.FirstOrDefault(t => t.Id == Id);
                var users = User.Identity.GetUserId();
                var userId = db.Users.FirstOrDefault(p => p.Id == users);
                var thisProject = userId.Projects.Select(p => p.Id);
                var thisticket = db.Tickets.Where(p => thisProject.Contains(p.ProjectId)).ToList();
                if (!ImageUploadValidator.IsWebFriendlyImage(image))
                {
                    ViewBag.ErrorMessage = "Please upload an image";

                }
                if (image == null)
                {
                    return HttpNotFound();
                }
                if (User.IsInRole("Admin") || (User.IsInRole("Project Manager") && thisticket.Any(p=> p.Id == Id)) || (User.IsInRole("Submitter") && tickets.CreatorId == users) || (User.IsInRole("Developer") && tickets.AssigneeId == users))
                {
                    var fileName = Path.GetFileName(image.FileName);
                    image.SaveAs(Path.Combine(Server.MapPath("~/Uploads/"), fileName));
                    ticketAttachment.FilePath = "/Uploads/" + fileName;
                    ticketAttachment.UserId = User.Identity.GetUserId();
                    ticketAttachment.Created = DateTime.Now;

                    ticketAttachment.TicketId = Id;
                    db.TicketAttachments.Add(ticketAttachment);
                    var personalEmailService = new PersonalEmailService();
                    var mailMessage = new MailMessage(
                       WebConfigurationManager.AppSettings["emailto"],
                       userId.Email
                       );
                    mailMessage.Body = "Hello";
                    mailMessage.Subject = "New Attachment";
                    mailMessage.IsBodyHtml = true;
                    personalEmailService.Send(mailMessage);
                    db.SaveChanges();
                }
                return RedirectToAction("Details", tickets);
            }
            return View(ticketAttachment);
        }

        // GET: Tickets/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Tickets tickets = db.Tickets.Find(id);
            if (tickets == null)
            {
                return HttpNotFound();
            }
            ViewBag.AssigneeId = new SelectList(db.Users, "Id", "DisplayName", tickets.AssigneeId);
            ViewBag.CreatorId = new SelectList(db.Users, "Id", "DisplayName", tickets.CreatorId);
            ViewBag.ProjectId = new SelectList(db.Projects, "Id", "Name", tickets.ProjectId);
            ViewBag.TicketPriorityId = new SelectList(db.TicketPriorities, "Id", "Name", tickets.TicketPriorityId);
            ViewBag.TicketStatusId = new SelectList(db.TicketStatuses, "Id", "Name", tickets.TicketStatusId);
            ViewBag.TicketTypeId = new SelectList(db.TicketTypes, "Id", "Name", tickets.TicketTypeId);
            return View(tickets);
        }

        // POST: Tickets/Edit/5
        // To protect from overposting attacks, please enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "Id,Name,Description,TicketTypeId,TicketPriorityId,CreatorId,TicketStatusId,AssigneeId,ProjectId")] Tickets tickets)
        {
            if (ModelState.IsValid)
            {
                
                var dateChanged = DateTimeOffset.Now;
                var changes = new List<TicketHistory>();
                var dbTicket = db.Tickets.First(p => p.Id == tickets.Id);
                dbTicket.Name = tickets.Name;
                dbTicket.Description = tickets.Description;
                dbTicket.TicketTypeId = tickets.TicketTypeId;
                dbTicket.Updated = dateChanged;
                var originalValues = db.Entry(dbTicket).OriginalValues;
                var currentValues = db.Entry(dbTicket).CurrentValues;
                foreach (var property in originalValues.PropertyNames)
                {
                    var originalValue = originalValues[property]?.ToString();
                    var currentValue = currentValues[property]?.ToString();
                    if (originalValue != currentValue)
                    {
                        var history = new TicketHistory();
                        history.Changed = dateChanged;
                        history.NewValue = currentValue;
                        history.OldValue = originalValue;
                        history.Property = property;
                        history.TicketId = dbTicket.Id;
                        history.UserId = User.Identity.GetUserId();
                        changes.Add(history);
                    }
                }
                db.TicketHistories.AddRange(changes);

                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.AssigneeId = new SelectList(db.Users, "Id", "DisplayName", tickets.AssigneeId);
            ViewBag.CreatorId = new SelectList(db.Users, "Id", "DisplayName", tickets.CreatorId);
            ViewBag.ProjectId = new SelectList(db.Projects, "Id", "Name", tickets.ProjectId);
            ViewBag.TicketPriorityId = new SelectList(db.TicketPriorities, "Id", "Name", tickets.TicketPriorityId);
            ViewBag.TicketStatusId = new SelectList(db.TicketStatuses, "Id", "Name", tickets.TicketStatusId);
            ViewBag.TicketTypeId = new SelectList(db.TicketTypes, "Id", "Name", tickets.TicketTypeId);
            return View(tickets);
        }

        // GET: Tickets/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Tickets tickets = db.Tickets.Find(id);
            if (tickets == null)
            {
                return HttpNotFound();
            }
            return View(tickets);
        }

        // POST: Tickets/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int? id)
        {
            Tickets tickets = db.Tickets.Find(id);
            db.Tickets.Remove(tickets);
            db.SaveChanges();
            return RedirectToAction("Index");
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
