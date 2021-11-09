using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using DPA.Sapewin.Domain.Entities;
using DPA.Sapewin.Repository;
using System.Linq;
using DPA.Sapewin.Domain.Models;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DPA.Sapewin.CalculationWorkflow.Application.Services
{
    public interface IAppointmentsService
    {
        Task<IEnumerable<Appointment>> ImportDirtyNotes(IEnumerable<Employee> employees,
                                           DateTime initial,
                                           DateTime final);
    }

    public class AppointmentsService : IAppointmentsService
    {
        private readonly IUnitOfWork<Appointment> _unitOfWorkAppointment;
        private readonly IUnitOfWork<DirtyNote> _unitOfWorkDirtyNote;
        private readonly IUnitOfWork<EletronicPointPairs> _unitOfWorkEletronicPointPairs;

        public AppointmentsService(IUnitOfWork<Appointment> unitOfWorkAppointment,
                                   IUnitOfWork<EletronicPointPairs> unitOfWorkEletronicPointPairs,
                                   IUnitOfWork<DirtyNote> unitOfWorkDirtyNote)
        {
            _unitOfWorkAppointment = unitOfWorkAppointment ?? throw new ArgumentNullException(nameof(unitOfWorkAppointment));
            _unitOfWorkEletronicPointPairs = unitOfWorkEletronicPointPairs ?? throw new ArgumentNullException(nameof(unitOfWorkEletronicPointPairs));
            _unitOfWorkDirtyNote = unitOfWorkDirtyNote ?? throw new ArgumentNullException(nameof(unitOfWorkDirtyNote));
        }

        public async IAsyncEnumerable<IGrouping<Employee, EletronicPointPairs>> SnapEmployeesAppointments(IEnumerable<IGrouping<Employee, EletronicPoint>> eletronicPointsByEmployee)
        {
            foreach (var eletronicPointsOfEmployee in eletronicPointsByEmployee)
                foreach (var eletronicPoint in eletronicPointsOfEmployee)
                    yield return new Grouping<Employee, EletronicPointPairs>(eletronicPointsOfEmployee.Key,
                                                                             await SnapEmployeeAppointments(eletronicPoint));
        }

        private async Task<IEnumerable<EletronicPointPairs>> SnapEmployeeAppointments(EletronicPoint eletronicPoint)
        {
            var appointments = eletronicPoint.Appointments.ToList();

            var rappointments = GetAppointmentsBasedInEletronicPoint(eletronicPoint);

            Appointment eappointment = GetEntryAppointment(rappointments,
                                                           ref appointments),

                        iiappointment = GetIntervalInAppointment(rappointments,
                                                                 eletronicPoint,
                                                                ref appointments),

                        ioappointment = GetIntervalOutAppointment(rappointments,
                                                                  eletronicPoint,
                                                                  ref appointments),

                        wappointment = GetWayOutAppointment(rappointments,
                                                            ref appointments);

            var elpointpairs = BuildEletronicPointPairs((eappointment,
                                                         iiappointment,
                                                         ioappointment,
                                                         wappointment), eletronicPoint)
                                                         .ToList();

            elpointpairs = ArrangeEletronicPointsPairs(elpointpairs,
                                                       appointments).ToList();

            await _unitOfWorkEletronicPointPairs.Repository.InsertAsync(elpointpairs);
            await _unitOfWorkEletronicPointPairs.SaveChangesAsync();

            return elpointpairs;
        }

        private IEnumerable<EletronicPointPairs> ArrangeEletronicPointsPairs(IList<EletronicPointPairs> eletronicPointPairs,
                                                                             IEnumerable<Appointment> appointments)
        {
            foreach (var appointment in appointments)
            {
                var npair = BuildNewPairBasedInAppointments(eletronicPointPairs, appointment);
                eletronicPointPairs.Add(npair);
            }

            return eletronicPointPairs;
        }

        private EletronicPointPairs BuildNewPairBasedInAppointments(IList<EletronicPointPairs> eletronicPointsPairs,
                                                                    Appointment appointment)
        {
            var bpair = eletronicPointsPairs.FirstOrDefault(x => VerifyIfIsBetweenCurrentAppointments(x, appointment)) ??
                            GetWithSmalllestDifferenceBasedInAppointment(eletronicPointsPairs, appointment);

            return BuildNewPairArrangePairAppointments(ref bpair, appointment);
        }

        private EletronicPointPairs GetWithSmalllestDifferenceBasedInAppointment(IList<EletronicPointPairs> eletronicPointPairs,
                                                                                 Appointment appointment) =>
            (from d in (from e in eletronicPointPairs
                        select new
                        {
                            Target = e,
                            Difference = Math.Abs(((e.OriginalEntry ?? e.OriginalWayOut).Date - appointment.Date).Ticks)
                        })
             orderby d.Difference
             select d.Target).FirstOrDefault();

        private EletronicPointPairs BuildNewPairArrangePairAppointments(ref EletronicPointPairs eletronicPointPairs,
                                                                        Appointment appointment)
        {
            var appointments = (from a in new[] { appointment,
                                                    eletronicPointPairs.OriginalEntry,
                                                    eletronicPointPairs.OriginalWayOut }
                                orderby a?.Date
                                select a).ToArray();

            eletronicPointPairs.OriginalEntry = appointments[0];
            eletronicPointPairs.EntryAppointmentId = appointments[0].Id;

            eletronicPointPairs.OriginalWayOut = appointments[1];
            eletronicPointPairs.WayOutAppointmentId = appointments[1].Id;

            return appointments[2] is null ? null : EnlaceAppointments(null,
                                                                       appointments[2],
                                                                       eletronicPointPairs.EletronicPoint);
        }


        private bool VerifyIfIsBetweenCurrentAppointments(EletronicPointPairs eletronicPointPairs, Appointment appointment) =>
            (eletronicPointPairs.OriginalEntry is null ||
            eletronicPointPairs.OriginalWayOut is null) ||
            eletronicPointPairs.OriginalEntry.Date <= appointment.Date &&
            eletronicPointPairs.OriginalWayOut.Date >= appointment.Date;

        private IEnumerable<EletronicPointPairs> BuildEletronicPointPairs((Appointment eappointment,
                                                                           Appointment iiapointment,
                                                                           Appointment ioappointment,
                                                                           Appointment wappointment) rappointments,
                                                                           EletronicPoint eletronicPoint) =>
            rappointments.iiapointment is not null ||
            rappointments.ioappointment is not null ?
                new[] { EnlaceAppointments(rappointments.eappointment,
                                            rappointments.iiapointment,
                                            eletronicPoint),
                         EnlaceAppointments(rappointments.ioappointment,
                                            rappointments.wappointment,
                                            eletronicPoint) } :

                new[] { EnlaceAppointments(rappointments.eappointment,
                                            rappointments.wappointment,
                                            eletronicPoint) };

        private EletronicPointPairs EnlaceAppointments(Appointment a1,
                                                       Appointment a2,
                                                       EletronicPoint eletronicPoint)
        {
            var eletronicPointPairs = new EletronicPointPairs
            {
                EmployeeId = eletronicPoint.EmployeeId,
                CompanyId = eletronicPoint.CompanyId,
                EletronicPointId = eletronicPoint.Id,
                EletronicPoint = eletronicPoint
            };

            var appointments = (from a in new[] { a1, a2 }
                                orderby a?.Date
                                select a).ToArray();

            if (appointments.Any(x => x is null))
                return BuildNullPair(ref eletronicPointPairs,
                                         eletronicPoint,
                                         appointments);

            eletronicPointPairs.OriginalEntry = appointments[0];
            eletronicPointPairs.EntryAppointmentId = appointments[0].Id;

            eletronicPointPairs.OriginalWayOut = appointments[1];
            eletronicPointPairs.WayOutAppointmentId = appointments[1].Id;

            return eletronicPointPairs;
        }

        private EletronicPointPairs BuildNullPair(ref EletronicPointPairs eletronicPointPairs,
                                                  EletronicPoint eletronicPoint,
                                                  Appointment[] appointments)
        {
            var nnappointment = appointments.FirstOrDefault(x => x is not null);
            var mappointment = GetOnlyMinutesFromDateTime(nnappointment.Date);

            if (Math.Abs(mappointment - eletronicPoint.Schedule.Period.Entry) <
                Math.Abs(mappointment - eletronicPoint.Schedule.Period.WayOut))
            {
                eletronicPointPairs.OriginalEntry = nnappointment;
                eletronicPointPairs.EntryAppointmentId = nnappointment.Id;
            }
            else
            {
                eletronicPointPairs.OriginalWayOut = nnappointment;
                eletronicPointPairs.WayOutAppointmentId = nnappointment.Id;
            }

            return eletronicPointPairs;
        }

        private int GetOnlyMinutesFromDateTime(DateTime? date) => !date.HasValue ? 0 : (date.Value.Hour * 60) + date.Value.Minute;

        private Appointment GetWayOutAppointment((DateTime eappointment,
                                                                DateTime iiapointment,
                                                                DateTime ioappointment,
                                                                DateTime wappointment) rappointments,
                                                                ref List<Appointment> appointments) =>

            GetBestAppointment(rappointments.wappointment,
                                ref appointments,
                                false,
                                rappointments);

        private Appointment GetIntervalInAppointment((DateTime eappointment,
                                                    DateTime iiapointment,
                                                    DateTime ioappointment,
                                                    DateTime wappointment) rappointments,
                                                    EletronicPoint eletronicPoint,
                                                    ref List<Appointment> appointments)
        {
            if (!eletronicPoint.Schedule.Period.IntervalIn.HasValue) return null;

            var iiappointment = GetBestAppointment(rappointments.iiapointment,
                                                  ref appointments,
                                                  true,
                                                  rappointments);

            if (iiappointment is null && eletronicPoint.Employee.Interval == EmployeeInterval.PreAssigned)
                iiappointment = new Appointment
                {
                    CompanyId = eletronicPoint.Employee.CompanyId,
                    EmployeeId = eletronicPoint.EmployeeId,
                    Date = eletronicPoint.Date
                            .AddMinutes(eletronicPoint.Schedule.Period.IntervalIn.Value)
                            .AddDays(eletronicPoint.Schedule.Period.IntervalIn.Value < eletronicPoint.Schedule.Period.Entry ? 1 : 0)
                };

            return iiappointment;
        }

        private Appointment GetIntervalOutAppointment((DateTime eappointment,
                                                    DateTime iiapointment,
                                                    DateTime ioappointment,
                                                    DateTime wappointment) rappointments,
                                                    EletronicPoint eletronicPoint,
                                                    ref List<Appointment> appointments)
        {
            if (!eletronicPoint.Schedule.Period.IntervalIn.HasValue) return null;

            var ioappointment = GetBestAppointment(rappointments.ioappointment,
                                                  ref appointments,
                                                  true,
                                                  rappointments);

            if (ioappointment is null && eletronicPoint.Employee.Interval == EmployeeInterval.PreAssigned)
                ioappointment = new Appointment
                {
                    CompanyId = eletronicPoint.Employee.CompanyId,
                    EmployeeId = eletronicPoint.EmployeeId,
                    Date = eletronicPoint.Date
                            .AddMinutes(eletronicPoint.Schedule.Period.IntervalOut.Value)
                            .AddDays(eletronicPoint.Schedule.Period.IntervalOut.Value < eletronicPoint.Schedule.Period.Entry ? 1 : 0)
                };

            return ioappointment;
        }

        private Appointment GetEntryAppointment((DateTime eappointment,
                                                                DateTime iiapointment,
                                                                DateTime ioappointment,
                                                                DateTime wappointment) rappointments,
                                                                ref List<Appointment> appointments) =>

            GetBestAppointment(rappointments.eappointment,
                                ref appointments,
                                false,
                                rappointments);


        private Appointment GetBestAppointment(DateTime refappointment,
                                               ref List<Appointment> appointments,
                                               bool verifyBestDateToo,
                                               (DateTime eappointment,
                                               DateTime iiapointment,
                                               DateTime ioappointment,
                                               DateTime wappointment) rappointments)
        {
            var bappointment = GetBestAppointmentByDateTime(refappointment, appointments);
            var bdate = GetBestDateByAppointment(bappointment,
                                                 rappointments.eappointment,
                                                 rappointments.iiapointment,
                                                 rappointments.ioappointment,
                                                 rappointments.wappointment);

            var r = !verifyBestDateToo ?
                        bappointment :

                            bdate == refappointment ?
                                bappointment : null;

            appointments.RemoveAll(x => x.Id == r?.Id);

            return r;
        }

        private DateTime GetBestDateByAppointment(Appointment appointment, params DateTime[] dates) =>
           (from d in (from dt in dates
                       select new
                       {
                           Target = dt,
                           Difference = Math.Abs((dt - appointment.Date).Ticks)
                       })
            orderby d.Difference
            select d.Target).FirstOrDefault();

        private Appointment GetBestAppointmentByDateTime(DateTime refappointment, IEnumerable<Appointment> appointments) =>
            (from d in (from a in appointments
                        select new
                        {
                            Target = a,
                            Difference = Math.Abs((a.Date - refappointment.Date).Ticks)
                        })
             orderby d.Difference
             select d.Target).FirstOrDefault();


        private (DateTime eappointment, DateTime iiapointment, DateTime ioappointment, DateTime wappointment) GetAppointmentsBasedInEletronicPoint(EletronicPoint eletronicPoint)
        {
            DateTime eappointment = eletronicPoint.Date.AddMinutes(eletronicPoint.Schedule.Period.Entry),
                     iiapointment = eletronicPoint.Date.AddMinutes(eletronicPoint.Schedule.Period.IntervalIn ?? 0),
                     ioappointment = eletronicPoint.Date.AddMinutes(eletronicPoint.Schedule.Period.IntervalOut ?? 0),
                     wappointment = eletronicPoint.Date.AddMinutes(eletronicPoint.Schedule.Period.WayOut);

            return (eappointment, iiapointment, ioappointment, wappointment);
        }

        public async Task<IEnumerable<Appointment>> ImportDirtyNotes(IEnumerable<Employee> employees,
                                           DateTime initial,
                                           DateTime final)
        {
            var dirtyNotes = _unitOfWorkDirtyNote.Repository
                                .GetAll(x =>
                                    employees.Any(z => z.Pis == x.Pis) &&
                                    x.Date >= initial && x.Date <= final).ToArray();

            var appointments = _unitOfWorkAppointment.Repository
                                    .GetAll(x =>
                                        employees.Any(y => y.Id == x.EmployeeId) &&
                                        x.Date >= initial && x.Date <= final).ToList();

            dirtyNotes = (from d in dirtyNotes
                          where !appointments.Any(x => x.UniqueAppointmentKey == d.UniqueAppointmentKey)
                          select d).ToArray();

            if (dirtyNotes.Any())
            {
                var ap = (from d in dirtyNotes select (Appointment)d)
                        .ToArray();

                await _unitOfWorkAppointment.Repository
                        .InsertAsync(ap);

                await _unitOfWorkAppointment.SaveChangesAsync();
                appointments.AddRange(ap);
            }

            return appointments;
        }
        public async IAsyncEnumerable<IGrouping<Employee, EletronicPointPairs>> SnapEmployeesAppointmentsLoad(IEnumerable<IGrouping<Employee, EletronicPoint>> eletronicPointsByEmployee)
        {
            foreach (var employee in eletronicPointsByEmployee)
                foreach (var point in employee)
                    yield return new Grouping<Employee, EletronicPointPairs>(employee.Key, await SnapEmployeeAppointmentsLoad(point));
        }
        private async Task<IEnumerable<EletronicPointPairs>> SnapEmployeeAppointmentsLoad(EletronicPoint point)
        {
            var appointments = point.Appointments.OrderBy(x => x.Date).ToList();
            var result = new List<EletronicPointPairs>();
                for (var i = 0; i < appointments.Count; i += 2)
                {
                    result.Add(i == appointments.Count - 1 
                    ? EnlaceEletronicPointPairsLoad(appointments[i], null, point) 
                    : EnlaceEletronicPointPairsLoad(appointments[i], appointments[i + 1], point));
                }

            await _unitOfWorkEletronicPointPairs.Repository.InsertAsync(result);
            await _unitOfWorkEletronicPointPairs.SaveChangesAsync();
            return result;
        }
        private EletronicPointPairs EnlaceEletronicPointPairsLoad(Appointment a1, Appointment a2, EletronicPoint ePoint)
        => HandleAppointments(new EletronicPointPairs {
                EmployeeId = ePoint.EmployeeId,
                CompanyId = ePoint.CompanyId,
                EletronicPointId = ePoint.Id,
                EletronicPoint = ePoint,
            }, a1, a2);
        private void HandleBothAppointmentsNotNull(ref EletronicPointPairs result, Appointment[] appointments)
        {
            result.OriginalEntry = appointments[0];
            result.EntryAppointmentId = appointments[0].Id;
            result.OriginalWayOut = appointments[1];
            result.WayOutAppointmentId = appointments[1].Id;
        }
        private void HandleNullAppointment(ref EletronicPointPairs result, Appointment[] appointments)
        {
            var owayout = appointments.FirstOrDefault(x => x is not null);
            result.OriginalWayOut = owayout;
            result.WayOutAppointmentId = owayout.Id;
        }
        private EletronicPointPairs HandleAppointments(EletronicPointPairs result, Appointment a1, Appointment a2)
        {
            var appointments =  new Appointment[] { a1, a2 }.OrderBy(x => x?.Date).ToArray();
            if (a1 is not null || a2 is not null)
            {
                if (a1 is not null && a2 is not null) HandleBothAppointmentsNotNull(ref result, appointments);
                else HandleNullAppointment(ref result, appointments);
            }
            return result;
        }
    }
}