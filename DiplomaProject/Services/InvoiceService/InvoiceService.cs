﻿using AutoMapper;
using DiplomaProject.DataTransferObjects;
using DiplomaProject.Models;
using DiplomaProject.Services.InvoiceServiceNS;
using DinkToPdf;
using DinkToPdf.Contracts;
using DiplomaProject.Services.PdfService;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DiplomaProject.Services.InvoiceServiceNS
{
    public class InvoiceDtos : IInvoiceService
    {
        private IMapper mapper;
        private readonly DiplomaProjectDbContext diplomaProjectDbContext;

        private readonly IReportService _reportService;

        public InvoiceDtos(IMapper mapper, DiplomaProjectDbContext diplomaProjectDbContext, IReportService reportService)
        {
            this.mapper = mapper;
            this.diplomaProjectDbContext = diplomaProjectDbContext;
            _reportService = reportService;
        }

        public async Task<IEnumerable<InvoiceDto>> Get()
        {
            var invoice = await this.diplomaProjectDbContext.Invoices.ToListAsync();
            return this.mapper.Map<List<Invoice>, List<InvoiceDto>>(invoice);
        }

        public async Task<InvoiceDto> Get(int id)
        {
            var invoice = await this.diplomaProjectDbContext.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null)
            {
                throw new ArgumentException("Id not existing");
            }

            return this.mapper.Map<InvoiceDto>(invoice);
        }

        public async Task<InvoiceDto> Create(InvoiceCreateDto invoiceCreateDto)
        {
            var invoice = this.mapper.Map<Invoice>(invoiceCreateDto);
            var existingInvoice = await this.diplomaProjectDbContext.Invoices.Where(i => i.ProjectId == invoiceCreateDto.ProjectId && i.Month == invoiceCreateDto.Month)
                .FirstOrDefaultAsync();
            if (existingInvoice != null)
            {
                throw new ArgumentException("An invoice with id number " + existingInvoice.InvoiceId + " already exists for project id " + existingInvoice.ProjectId
                    + " and month " + existingInvoice.Month);
            }

            await this.diplomaProjectDbContext.AddAsync(invoice);
            await this.diplomaProjectDbContext.SaveChangesAsync();
            var timeEntries = await this.diplomaProjectDbContext.TimeEntries.Where(te => te.ProjectId == invoiceCreateDto.ProjectId && te.Date.Month == invoiceCreateDto.Month).ToListAsync();
            decimal totalAmountOfHours = 0;
            foreach (var timeEntry in timeEntries)
            {
                timeEntry.InvoiceId = invoice.InvoiceId; 
                totalAmountOfHours = totalAmountOfHours + timeEntry.AmountOfHours;
            }

            Project project = await this.diplomaProjectDbContext.Projects.Where(p => p.ProjectId == invoice.ProjectId).FirstOrDefaultAsync();
            invoice.TotalToPay = totalAmountOfHours * project.PricePerHour;
            invoice.AmountOfHours = totalAmountOfHours;
            await this.diplomaProjectDbContext.SaveChangesAsync();
            return this.mapper.Map<InvoiceDto>(invoice);
        }

        public async Task<InvoiceDto> Update(int id, InvoiceUpdateDto invoiceUpdateDto)
        {
            var invoice = await this.diplomaProjectDbContext.Invoices.Include(i => i.Project).FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null)
            {
                throw new ArgumentException("Id not existing");
            }

            invoice.Date = invoiceUpdateDto.Date;
            invoice.DueDate = invoiceUpdateDto.DueDate;
            invoice.Month = invoiceUpdateDto.Month;
            invoice.Discount = invoiceUpdateDto.Discount;
            invoice.Tax = invoiceUpdateDto.Tax;
            invoice.TotalToPay = RecalculateInvoice(invoice);
            invoice.IsInvoicePaid = invoice.IsInvoicePaid;
            this.diplomaProjectDbContext.Update(invoice);
            await this.diplomaProjectDbContext.SaveChangesAsync();
            return this.mapper.Map<InvoiceDto>(invoice);
        }

        public async Task<InvoiceDto> Delete(int id)
        {
            var invoice = await this.diplomaProjectDbContext.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == id);
            if (invoice == null)
            {
                throw new ArgumentException("Id not existing");
            }

            var timeEntries = await this.diplomaProjectDbContext.TimeEntries.Where(te => te.InvoiceId == id).ToListAsync();
            foreach (var element in timeEntries)
            {
                element.InvoiceId = null;
            }

            this.diplomaProjectDbContext.Remove(invoice);
            await this.diplomaProjectDbContext.SaveChangesAsync();
            return null;
        }

        public async Task<IEnumerable<InvoiceDto>> GetAllInvoicesForGivenProject(int projectId)
        {
            var allInvoicesForGivenProject = await this.diplomaProjectDbContext.Invoices.Where(i => i.ProjectId == projectId).ToListAsync();
            return this.mapper.Map<List<Invoice>, List<InvoiceDto>>(allInvoicesForGivenProject).OrderBy(i => i.InvoiceId);
        }

        public async Task<IEnumerable<InvoiceForClientDto>> GetInvoicesForProjectsPerClient(int clientId)
        {
            var invoicesForProjectsPerClient = await this.diplomaProjectDbContext.Invoices.Where(i => i.Project.ClientId == clientId)
                .Include(i => i.Project.Client).ToListAsync();
            if (invoicesForProjectsPerClient.Count == 0)
            {
                throw new ArgumentException("Id not existing");
            }
            return this.mapper.Map<List<Invoice>, List<InvoiceForClientDto>>(invoicesForProjectsPerClient).OrderBy(i => i.InvoiceId);
        }

        public async Task<IEnumerable<InvoiceForTimeEntryDto>> GetInvoiceWithTimeEntriesPerProject(int projectId)
        {
            var invoiceWithAllTimeEntriesPerProject = await this.diplomaProjectDbContext.Invoices.Where(i => i.ProjectId == projectId)
                .Include(i => i.TimeEntries).Include(i => i.Project).ToListAsync();
            if (invoiceWithAllTimeEntriesPerProject.Count == 0)
            {
                throw new ArgumentException("Invoice for a given project is not existing");
            }

            var result = new List<InvoiceForTimeEntryDto>();

            foreach (var invoice in invoiceWithAllTimeEntriesPerProject)
            {
                var invoiceForTimeEntriesDto = new InvoiceForTimeEntryDto()
                {
                    ProjectId = invoice.ProjectId,
                    ProjectName = invoice.Project.ProjectName,
                    InvoiceId = invoice.InvoiceId,
                    Date = invoice.Date,
                    DueDate = invoice.DueDate,
                    Month = invoice.Month,
                    Discount = invoice.Discount,
                    Tax = invoice.Tax,
                    TotalToPay = invoice.TotalToPay,
                    IsInvoicePaid = invoice.IsInvoicePaid,
                    TimeEntries = new List<TimeEntryDto>(),
                };

                foreach (var timeEntry in invoice.TimeEntries)
                {
                    var timeEntriesDto = new TimeEntryDto()
                    {
                        TimeEntryId = timeEntry.TimeEntryId,
                        Date = timeEntry.Date,
                        AmountOfHours = timeEntry.AmountOfHours,
                        Comment = timeEntry.Comment,
                    };
                    invoiceForTimeEntriesDto.TimeEntries.Add(timeEntriesDto);
                };
                result.Add(invoiceForTimeEntriesDto);
            }
            return result;
        }

        public byte[] GenerateInvoicePdf(int id)
        {
            var invoice = this.diplomaProjectDbContext.Invoices.Include(i => i.Project.Client).Include(i => i.Project.TimeEntries).FirstOrDefault(i => i.InvoiceId == id);
            if (invoice == null)
            {
                throw new ArgumentException("Invoice with a given id = " + id + " is not existing");
            }

            var invoiceData = new TemplateForInvoicePdf()
            {
                ClientName = invoice.Project.Client.ClientName,
                BuildingNumber = invoice.Project.Client.BuildingNumber,
                StreetName = invoice.Project.Client.StreetName,
                City = invoice.Project.Client.City,
                PostCode = invoice.Project.Client.PostCode,
                Country = invoice.Project.Client.Country,
                ProjectName = invoice.Project.ProjectName,
                Month = invoice.Month,
                AmountOdHours = invoice.AmountOfHours,
                PricePerHour = invoice.Project.PricePerHour,
                TotalToPay = invoice.TotalToPay,
                Date = invoice.Date,
                TimeEntries = new List<TimeEntryDto>(),
            };

            foreach (var timeEntry in invoice.TimeEntries)
            {
                var timeEntriesDto = new TimeEntryDto()
                {
                    TimeEntryId = timeEntry.TimeEntryId,
                    Date = timeEntry.Date,
                    AmountOfHours = timeEntry.AmountOfHours,
                    Comment = timeEntry.Comment,
                };
                invoiceData.TimeEntries.Add(timeEntriesDto);
            };

            var pdfFile = _reportService.GenerateInvoicePdf(invoiceData, id);
            return pdfFile;
        }
        public async Task<InvoicePeriodClosureDto> CloseInvoicePeriod(int invoiceId)
        {
            var invoice = await this.diplomaProjectDbContext.Invoices.FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
            InvoiceOperations.CloseInvoicePeriod(invoice);
            await this.diplomaProjectDbContext.SaveChangesAsync();
            return this.mapper.Map<InvoicePeriodClosureDto>(invoice);
        }

        public bool CheckIfInvoicePeriodIsClosed(int month, long projectId)
        {
            var invoice = this.diplomaProjectDbContext.Invoices.Include(i => i.TimeEntries).Where(i => i.ProjectId == projectId
                            && i.Month == month).FirstOrDefault();

            if (invoice != null)
            {
                return invoice.IsInvoicePeriodClosed;
            }
            return false;
        }

        private decimal RecalculateInvoice(Invoice invoice)
        {
            var timeEntries = this.diplomaProjectDbContext.TimeEntries.Where(te => te.Date.Month == invoice.Month).ToList();
            return InvoiceOperations.CalculateInvoiceCost(timeEntries, invoice);
        }
    }
}
