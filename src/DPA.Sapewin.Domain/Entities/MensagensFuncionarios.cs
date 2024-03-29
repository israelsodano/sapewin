﻿using System;

namespace DPA.Sapewin.Domain.Entities
{
    public class MensagensFuncionarios
    {
        public int IDMensagem { get; set; }

        public Guid EmployeeId { get; set; }

        public Guid CompanyId { get; set; }

        public DateTime DataInicial { get; set; }

        public DateTime? DataFinal { get; set; }

        public Mensagem Mensagem { get; set; }

        public Employee Funcionario { get; set; }

        public Company Empresa { get; set; }
    }
}