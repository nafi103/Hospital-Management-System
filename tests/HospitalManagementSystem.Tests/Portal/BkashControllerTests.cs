using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HospitalManagementSystem.Controllers;
using HospitalManagementSystem.Models;
using HospitalManagementSystem.Models.Dto;
using HospitalManagementSystem.Services;
using HospitalManagementSystem.Tests.Fixtures;
using HospitalManagementSystem.Tests.Helpers;
using Xunit;

namespace HospitalManagementSystem.Tests.Portal
{
    public class MockBkashPaymentService : IBkashPaymentService
    {
        public Func<Task<string?>>? GrantTokenHandler { get; set; }
        public Func<int, decimal, string, string, Task<BkashCreatePaymentResponse?>>? CreatePaymentHandler { get; set; }
        public Func<string, Task<BkashExecutePaymentResponse?>>? ExecutePaymentHandler { get; set; }

        public Task<string?> GrantTokenAsync() =>
            GrantTokenHandler != null ? GrantTokenHandler() : Task.FromResult<string?>("mock_token");

        public Task<BkashCreatePaymentResponse?> CreatePaymentAsync(int billId, decimal amount, string payerReference, string callbackUrl) =>
            CreatePaymentHandler != null 
                ? CreatePaymentHandler(billId, amount, payerReference, callbackUrl)
                : Task.FromResult<BkashCreatePaymentResponse?>(new BkashCreatePaymentResponse
                {
                    StatusCode = "0000",
                    PaymentID = "MOCK_PAY_" + billId,
                    BkashURL = "https://sandbox.bka.sh/checkout?paymentID=MOCK_PAY_" + billId
                });

        public Task<BkashExecutePaymentResponse?> ExecutePaymentAsync(string paymentId) =>
            ExecutePaymentHandler != null
                ? ExecutePaymentHandler(paymentId)
                : Task.FromResult<BkashExecutePaymentResponse?>(new BkashExecutePaymentResponse
                {
                    StatusCode = "0000",
                    PaymentID = paymentId,
                    TrxID = "TRX_MOCK_123",
                    TransactionStatus = "Completed",
                    Amount = "1000.00",
                    MerchantInvoiceNumber = "BILL-200"
                });
    }

    public class BkashControllerTests
    {
        private async Task<(ApplicationDbContext Context, Patient ParentA, Patient ChildA, Patient ParentB, Bill BillParentA, Bill BillChildA, Bill BillParentB)> SetupEnvironmentAsync()
        {
            var context = await TestDbContextFactory.CreateSeededDbContextAsync();

            // Configure Parent A (Id: 100, UserId: 1000) & Child A (Id: 101, Guardian: 100)
            var parentA = await context.Patients.FindAsync(100);
            parentA!.UserId = 1000;

            var childA = await context.Patients.FindAsync(101);
            childA!.GuardianPatientId = 100;
            childA.GuardianRelationship = "Father";

            // Configure Parent B (Id: 102, UserId: 1002)
            var parentB = new Patient
            {
                Id = 102,
                Uhid = "PT-202610-0102",
                FullName = "Parent B",
                UserId = 1002,
                DateOfBirth = new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Gender = "Female",
                BloodGroup = "O+",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            context.Patients.Add(parentB);

            // Bill for Parent A: Subtotal 1000, Unpaid
            var billParentA = new Bill
            {
                Id = 200,
                PatientId = parentA.Id,
                SubtotalAmount = 1000m,
                DiscountAmount = 0m,
                NetTotal = 1000m,
                PaidAmount = 0m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            billParentA.BillItems.Add(new BillItem { Department = DepartmentType.Consultation, Description = "Consultation", Amount = 1000m });

            // Bill for Child A: Subtotal 500, Unpaid
            var billChildA = new Bill
            {
                Id = 201,
                PatientId = childA.Id,
                SubtotalAmount = 500m,
                DiscountAmount = 0m,
                NetTotal = 500m,
                PaidAmount = 0m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            billChildA.BillItems.Add(new BillItem { Department = DepartmentType.General, Description = "Pediatric Care", Amount = 500m });

            // Bill for Parent B: Subtotal 800, Unpaid
            var billParentB = new Bill
            {
                Id = 202,
                PatientId = parentB.Id,
                SubtotalAmount = 800m,
                DiscountAmount = 0m,
                NetTotal = 800m,
                PaidAmount = 0m,
                Status = BillStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            billParentB.BillItems.Add(new BillItem { Department = DepartmentType.General, Description = "Blood Test", Amount = 800m });

            context.Bills.AddRange(billParentA, billChildA, billParentB);
            await context.SaveChangesAsync();

            return (context, parentA, childA, parentB, billParentA, billChildA, billParentB);
        }

        private BkashController CreateController(
            ApplicationDbContext context,
            IBkashPaymentService service,
            ClaimsPrincipal user)
        {
            var controller = new BkashController(context, service, NullLogger<BkashController>.Instance);
            ControllerTestHelper.SetupController(controller, user);
            return controller;
        }

        [Fact]
        public async Task Pay_StrangerBill_ReturnsForbid()
        {
            // Arrange: Parent A attempts to initiate payment on Parent B's bill
            var (context, parentA, _, _, _, _, billParentB) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService();
            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Pay(billParentB.Id);

            // Assert: Zero-tolerance IDOR enforcement
            Assert.IsType<ForbidResult>(result);
        }

        [Fact]
        public async Task Pay_FullyPaidBill_RedirectsWithErrorMessage()
        {
            // Arrange: Parent A's bill is already settled
            var (context, parentA, _, _, billParentA, _, _) = await SetupEnvironmentAsync();
            billParentA.PaidAmount = 1000m;
            billParentA.Status = BillStatus.Paid;
            await context.SaveChangesAsync();

            var service = new MockBkashPaymentService();
            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Pay(billParentA.Id);

            // Assert: Redirected to Portal/Bills with error
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);
            Assert.Equal("Portal", redirect.ControllerName);
            Assert.Equal("This invoice has already been fully paid.", controller.TempData["ErrorMessage"]);
        }

        [Fact]
        public async Task Pay_OwnBill_InitiatesPaymentAndRedirectsToBkashUrl()
        {
            // Arrange
            var (context, parentA, _, _, billParentA, _, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService
            {
                CreatePaymentHandler = (billId, amount, payerRef, callbackUrl) =>
                {
                    Assert.Equal(200, billId);
                    Assert.Equal(1000m, amount);
                    return Task.FromResult<BkashCreatePaymentResponse?>(new BkashCreatePaymentResponse
                    {
                        StatusCode = "0000",
                        PaymentID = "PAY_TEST_200",
                        BkashURL = "https://sandbox.bka.sh/checkout?paymentID=PAY_TEST_200"
                    });
                }
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Pay(billParentA.Id);

            // Assert: Redirects to bKash gateway URL
            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://sandbox.bka.sh/checkout?paymentID=PAY_TEST_200", redirect.Url);
        }

        [Fact]
        public async Task Pay_DependentChildBill_PermittedAndRedirectsToBkashUrl()
        {
            // Arrange: Parent A initiates payment on linked Child A's bill
            var (context, parentA, childA, _, _, billChildA, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService
            {
                CreatePaymentHandler = (billId, amount, payerRef, callbackUrl) =>
                {
                    Assert.Equal(201, billId);
                    Assert.Equal(500m, amount);
                    return Task.FromResult<BkashCreatePaymentResponse?>(new BkashCreatePaymentResponse
                    {
                        StatusCode = "0000",
                        PaymentID = "PAY_CHILD_201",
                        BkashURL = "https://sandbox.bka.sh/checkout?paymentID=PAY_CHILD_201"
                    });
                }
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Pay(billChildA.Id);

            // Assert: Access granted for dependent child
            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://sandbox.bka.sh/checkout?paymentID=PAY_CHILD_201", redirect.Url);
        }

        [Fact]
        public async Task Callback_Cancelled_RedirectsWithWarning()
        {
            // Arrange
            var (context, parentA, _, _, _, _, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService();
            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Callback(paymentID: "PAY123", status: "cancel");

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);
            Assert.Equal("Portal", redirect.ControllerName);
            Assert.Contains("cancelled", controller.TempData["WarningMessage"]?.ToString());
        }

        [Fact]
        public async Task Callback_Failure_RedirectsWithErrorMessage()
        {
            // Arrange
            var (context, parentA, _, _, _, _, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService();
            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Callback(paymentID: "PAY123", status: "failure");

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);
            Assert.Equal("Portal", redirect.ControllerName);
            Assert.Contains("failed", controller.TempData["ErrorMessage"]?.ToString());
        }

        [Fact]
        public async Task Callback_Success_AtomicallyRecordsTransactionAndRecalculatesTotals()
        {
            // Arrange: Payment execution completes successfully for Bill 200 (Amount: 1000)
            var (context, parentA, _, _, billParentA, _, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService
            {
                ExecutePaymentHandler = paymentId => Task.FromResult<BkashExecutePaymentResponse?>(new BkashExecutePaymentResponse
                {
                    StatusCode = "0000",
                    StatusMessage = "Successful",
                    PaymentID = paymentId,
                    TrxID = "TRX_BKASH_999888",
                    TransactionStatus = "Completed",
                    Amount = "1000.00",
                    MerchantInvoiceNumber = "BILL-200"
                })
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Callback(paymentID: "PAY_SETTLE_200", status: "success");

            // Assert: Redirect to Bills with confirmation
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);
            Assert.Equal("Portal", redirect.ControllerName);
            Assert.Contains("TRX_BKASH_999888", controller.TempData["SuccessMessage"]?.ToString());

            // Verify Ledger state
            var updatedBill = await context.Bills
                .Include(b => b.PaymentTransactions)
                .FirstAsync(b => b.Id == 200);

            Assert.Equal(1000m, updatedBill.PaidAmount);
            Assert.Equal(BillStatus.Paid, updatedBill.Status);

            var tx = Assert.Single(updatedBill.PaymentTransactions);
            Assert.Equal(1000m, tx.Amount);
            Assert.Equal("bKash", tx.PaymentMethod);
            Assert.Equal("TRX_BKASH_999888", tx.TransactionId);
            Assert.Contains("TRX_BKASH_999888", tx.Notes);
        }

        [Fact]
        public async Task Callback_Success_IdempotencyPreventsDuplicateTransaction()
        {
            // Arrange: Pre-record transaction TRX_EXISTING_777 for Bill 200
            var (context, parentA, _, _, billParentA, _, _) = await SetupEnvironmentAsync();

            var initialTx = new PaymentTransaction
            {
                BillId = billParentA.Id,
                Amount = 1000m,
                PaymentMethod = "bKash",
                TransactionId = "TRX_EXISTING_777",
                TransactionDate = DateTime.UtcNow,
                Notes = "First receipt"
            };
            context.PaymentTransactions.Add(initialTx);
            billParentA.PaidAmount = 1000m;
            billParentA.Status = BillStatus.Paid;
            await context.SaveChangesAsync();

            var service = new MockBkashPaymentService
            {
                ExecutePaymentHandler = paymentId => Task.FromResult<BkashExecutePaymentResponse?>(new BkashExecutePaymentResponse
                {
                    StatusCode = "0000",
                    PaymentID = paymentId,
                    TrxID = "TRX_EXISTING_777", // Same TrxID!
                    TransactionStatus = "Completed",
                    Amount = "1000.00",
                    MerchantInvoiceNumber = "BILL-200"
                })
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act: Gateway or patient retries callback
            var result = await controller.Callback(paymentID: "PAY_RELOAD_200", status: "success");

            // Assert: Handled idempotently
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);
            Assert.Contains("already recorded", controller.TempData["SuccessMessage"]?.ToString());

            // Verify no duplicate transactions inserted
            var transactions = await context.PaymentTransactions.Where(t => t.BillId == 200).ToListAsync();
            Assert.Single(transactions);
            Assert.Equal(1000m, (await context.Bills.FindAsync(200))!.PaidAmount);
        }

        [Fact]
        public async Task Callback_Success_ParentCanPayForDependentChild()
        {
            // Arrange: Callback received for Child A's bill (Bill 201)
            var (context, parentA, _, _, _, billChildA, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService
            {
                ExecutePaymentHandler = paymentId => Task.FromResult<BkashExecutePaymentResponse?>(new BkashExecutePaymentResponse
                {
                    StatusCode = "0000",
                    PaymentID = paymentId,
                    TrxID = "TRX_CHILD_SETTLED_555",
                    TransactionStatus = "Completed",
                    Amount = "500.00",
                    MerchantInvoiceNumber = "BILL-201"
                })
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Callback(paymentID: "PAY_CHILD_201", status: "success");

            // Assert: Child's bill marked paid
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Bills", redirect.ActionName);

            var updatedChildBill = await context.Bills
                .Include(b => b.PaymentTransactions)
                .FirstAsync(b => b.Id == 201);

            Assert.Equal(500m, updatedChildBill.PaidAmount);
            Assert.Equal(BillStatus.Paid, updatedChildBill.Status);
            Assert.Single(updatedChildBill.PaymentTransactions);
            Assert.Equal("TRX_CHILD_SETTLED_555", updatedChildBill.PaymentTransactions.First().TransactionId);
        }

        [Fact]
        public async Task Callback_CrossTenantIDOR_Forbidden()
        {
            // Arrange: Payment executed for Parent B's bill (Bill 202), but caller is Parent A
            var (context, parentA, _, _, _, _, _) = await SetupEnvironmentAsync();
            var service = new MockBkashPaymentService
            {
                ExecutePaymentHandler = paymentId => Task.FromResult<BkashExecutePaymentResponse?>(new BkashExecutePaymentResponse
                {
                    StatusCode = "0000",
                    PaymentID = paymentId,
                    TrxID = "TRX_MALICIOUS_444",
                    TransactionStatus = "Completed",
                    Amount = "800.00",
                    MerchantInvoiceNumber = "BILL-202" // Bill belongs to Parent B!
                })
            };

            var user = TestPrincipalFactory.CreatePatient(patientUserId: 1000, uhid: parentA.Uhid);
            var controller = CreateController(context, service, user);

            // Act
            var result = await controller.Callback(paymentID: "PAY_CROSS_202", status: "success");

            // Assert: Blocked by IDOR guard
            Assert.IsType<ForbidResult>(result);
            Assert.Empty(await context.PaymentTransactions.Where(t => t.TransactionId == "TRX_MALICIOUS_444").ToListAsync());
        }
    }
}
