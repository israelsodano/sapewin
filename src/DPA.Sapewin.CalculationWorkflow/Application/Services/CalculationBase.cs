using System;
using System.Collections.Generic;
using System.Linq;
using DPA.Sapewin.Domain.Entities;
using DPA.Sapewin.Domain.Models;

namespace DPA.Sapewin.CalculationWorkflow.Application.Services
{
    public abstract class CalculationBase
    {
        protected readonly IAppointmentsService _appointmentsService;
        public CalculationBase(IAppointmentsService appointmentsService) =>
            _appointmentsService = appointmentsService ?? throw new ArgumentNullException(nameof(appointmentsService));

        protected List<Appointment> GetNotNullAppointments(IEnumerable<EletronicPointPairs> pairs)
        => (from p in pairs.SelectMany(x => x.GetAppointments())
            where p is not null
            select p).ToList();

        protected IEnumerable<RelationAppointmetDate> GetRelationAppointmetDates(List<Appointment> appointments, DateTime[] intervals)
        => from ea in intervals
           select new RelationAppointmetDate(_appointmentsService.GetBestAppointment(ea,
                                                           ref appointments,
                                                           true,
                                                           intervals), ea);
        protected Appointment GetIntervalInAppointment((DateTime eappointment,
                                                  DateTime iiapointment,
                                                  DateTime ioappointment,
                                                  DateTime wappointment) rappointments,
                                                  List<Appointment> appointments) =>
        _appointmentsService.GetBestAppointment(rappointments.iiapointment,
                                                ref appointments,
                                                true,
                                                rappointments.eappointment,
                                                rappointments.iiapointment,
                                                rappointments.ioappointment,
                                                rappointments.wappointment);

        protected Appointment GetIntervalOutAppointment((DateTime eappointment,
                                              DateTime iiapointment,
                                              DateTime ioappointment,
                                              DateTime wappointment) rappointments,
                                              List<Appointment> appointments) =>
        _appointmentsService.GetBestAppointment(rappointments.ioappointment,
                                            ref appointments,
                                            true,
                                            rappointments.eappointment,
                                            rappointments.iiapointment,
                                            rappointments.ioappointment,
                                            rappointments.wappointment);

    }
}