using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class InvoiceRepository : GenericRepository<Invoice>, IInvoiceRepository
{
    public InvoiceRepository(ApplicationDbContext context) : base(context)
    {
    }
}
