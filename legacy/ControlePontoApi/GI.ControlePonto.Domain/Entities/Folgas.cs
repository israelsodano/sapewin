﻿using System;

namespace GI.ControlePonto.Domain.Entities
{
    public class Folgas
    {
        public virtual int IDFolga { get; set; }

        public virtual long IDFuncionario { get; set; }

        public virtual int IDEmpresa { get; set; }

        public virtual DateTime Data { get; set; }

        public virtual Funcionarios Funcionario { get; set; }
    }
}