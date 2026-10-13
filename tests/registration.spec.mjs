import { test, expect } from '@playwright/test';
import pg from 'pg';
import fs from 'node:fs';
import zlib from 'node:zlib';
const secrets=JSON.parse(fs.readFileSync('.local/test-settings.json'));
const db=new pg.Client({connectionString:process.env.TEST_DATABASE_URL??'postgresql://postgres@127.0.0.1:55432/safetunnels_test'});
// Synthetic checksum-valid values, not customer banking data.
const mod97=s=>[...s].reduce((r,c)=>(r*10+Number(c))%97,0);
// IBANs must be unique across registrations, so every account gets a fresh one.
function newIban(){
  const bban='999'+Array.from({length:19},()=>Math.floor(Math.random()*10)).join('');
  return 'IR'+String(98-mod97(bban+'182700')).padStart(2,'0')+bban;
}
let seq=0,iban=newIban(); // iban: first account of the most recent fill()
test.beforeAll(async()=>{
  await db.connect();
  const name=(await db.query('SELECT current_database() AS name')).rows[0].name;
  if(name!=='safetunnels_test')throw Error('Tests require the dedicated safetunnels_test database.');
});
test.afterAll(async()=>db.end());
async function fill(page,count=1,ibans=[]){
  ibans=Array.from({length:count},(_,i)=>ibans[i]??newIban());iban=ibans[0];
  await page.goto('/');
  const firstName='آزمایش '+Date.now()+'-'+(++seq);
  await page.locator('[name="Input.FirstName"]').fill(firstName);
  await page.locator('[name="Input.LastName"]').fill('کاربر ساختگی');
  for(let i=0;i<count;i++){
    if(i)await page.locator('#add-account').click();
    await page.locator(`[name="Input.Accounts[${i}].BankName"]`).selectOption('سایر بانک‌ها');
    await page.locator(`[name="Input.Accounts[${i}].OtherBankName"]`).fill('بانک آزمایشی');
    await page.locator(`[name="Input.Accounts[${i}].IBAN"]`).fill(ibans[i]);
  }
  return firstName;
}
// Reads the worksheet out of a downloaded .xlsx (a zip) via its local file header.
function sheetXml(zip){
  const at=zip.indexOf('xl/worksheets/sheet1.xml')-30,start=at+30+zip.readUInt16LE(at+26)+zip.readUInt16LE(at+28);
  return zlib.inflateRawSync(zip.subarray(start,start+zip.readUInt32LE(at+18))).toString('utf8');
}
async function data(page){return page.locator('form').evaluate(f=>Object.fromEntries(new FormData(f)));}
test('ثبت شبا، همراه خالی و حذف فیلدهای کارت و حساب',async({page})=>{
  const firstName=await fill(page);
  await expect(page.locator('[name*=CardNumber], [name*=AccountNumber]')).toHaveCount(0);
  await page.locator('#submit-button').click();
  await expect(page).toHaveURL(/\/Success$/);
  await expect(page.locator('h1')).toHaveText('اطلاعات شما با موفقیت ثبت شد.');
  const result=await db.query('SELECT a."Mobile", a."TrackingCode", b.* FROM "Applicants" a JOIN "BankAccounts" b ON b."ApplicantId"=a."Id" WHERE a."FirstName"=$1',[firstName]);
  expect(result.rowCount).toBe(1);expect(result.rows[0].Mobile).toBeNull();
  // The tracking code is still stored, but no longer shown to the applicant.
  expect(result.rows[0].TrackingCode).toMatch(/^[A-F0-9]{32}$/);
  await expect(page.locator('.tracking-code')).toHaveCount(0);
  expect(await page.content()).not.toContain(result.rows[0].TrackingCode);
  expect(result.rows[0].IBAN).toMatch(/^v1:local1:/);
  expect(result.rows[0].BankName).toBe('بانک آزمایشی');
  expect(result.rows[0]).not.toHaveProperty('CardLastFour');
  expect(result.rows[0]).not.toHaveProperty('AccountNumber');
  expect(JSON.stringify(result.rows)).not.toContain(iban);
});
test('ثبت چند حساب و نرمال‌سازی ارقام فارسی',async({page})=>{
  const firstName=await fill(page,3);
  await page.locator('[name="Input.Mobile"]').fill('+۹۸۹۱۲۳۴۵۶۷۸۹');
  await page.locator('[name="Input.Accounts[0].IBAN"]').fill(iban.slice(2).replace(/\d/g,d=>'۰۱۲۳۴۵۶۷۸۹'[+d]));
  await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
  const r=await db.query('SELECT "Mobile",(SELECT count(*) FROM "BankAccounts" b WHERE b."ApplicantId"=a."Id") AS count FROM "Applicants" a WHERE "FirstName"=$1',[firstName]);
  expect(r.rows[0].count).toBe('3');expect(r.rows[0].Mobile).toBe('09123456789');
});
for(const [field,value] of [['IBAN','IR00000000000000000000000000'],['Mobile','123']]){
 test('رد '+field+' در مرورگر و سرور',async({page})=>{
  await fill(page);const name=field==='Mobile'?'Input.Mobile':`Input.Accounts[0].${field}`;
  await page.locator(`[name="${name}"]`).fill(value);await page.locator('#submit-button').click();
  await expect(page.locator(`[name="${name}"]`)).toHaveAttribute('aria-invalid','true');
  const payload=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
  const response=await page.request.post('/',{form:payload});expect(response.status()).toBe(200);expect(await response.text()).toContain('field-validation-error');
  expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
 });
}
test('افزودن، حذف، بازشماره‌گذاری و سقف ده حساب',async({page})=>{
 await fill(page,3);await page.locator('[data-account]').nth(1).locator('.remove-account').click();
 await expect(page.locator('[data-account]')).toHaveCount(2);
 await expect(page.locator('[name="Input.Accounts[1].BankName"]')).toHaveValue('سایر بانک‌ها');
 for(let i=2;i<10;i++)await page.locator('#add-account').click();
 await expect(page.locator('#add-account')).toBeDisabled();await expect(page.locator('[data-account]')).toHaveCount(10);
});
test('سرور ثبت بدون حساب و بیش از ده حساب را رد می‌کند',async({page})=>{
 await fill(page);const base=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 const empty={...base};for(const k of Object.keys(empty))if(k.startsWith('Input.Accounts'))delete empty[k];
 const response=await page.request.post('/',{form:empty});expect(response.status()).toBe(200);
 const tooMany={...base};for(let i=1;i<11;i++)for(const f of ['BankName','OtherBankName','IBAN'])tooMany[`Input.Accounts[${i}].${f}`]=base[`Input.Accounts[0].${f}`];
 const over=await page.request.post('/',{form:tooMany});expect(over.status()).toBe(400);
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
});
test('ارسال هم‌زمان یک درخواست فقط یک ثبت ایجاد می‌کند',async({page})=>{
 await fill(page,2);const payload=await data(page);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 const responses=await Promise.all(Array.from({length:8},()=>page.request.post('/',{form:payload,maxRedirects:0})));
 responses.forEach(r=>expect(r.status()).toBe(302));
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before+1);
});
test('افراد هم‌نام با درخواست مستقل ثبت می‌شوند',async({page})=>{
 const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 for(let i=0;i<2;i++){
  await fill(page);await page.locator('[name="Input.FirstName"]').fill('نام یکسان');
  await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 }
 expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before+2);
});
test('فرم فقط به مبدأ خود درخواست می‌فرستد و نام قابل اجرای HTML نیست',async({page})=>{
 const requests=[];page.on('request',r=>requests.push(r.url()));
 await fill(page);await page.locator('[name="Input.FirstName"]').fill('<script>window.testXss=1</script>');
 const payload=await data(page);payload['Input.Accounts[0].IBAN']='IR00';
 const r=await page.request.post('/',{form:payload});expect(await r.text()).not.toContain('<script>window.testXss=1</script>');
 expect(requests.every(u=>new URL(u).origin===new URL(page.url()).origin)).toBe(true);
 expect(await page.evaluate(()=>window.testXss)).toBeUndefined();
});
test('خرابی ذخیره حساب کل تراکنش را rollback می‌کند و فرم حفظ می‌شود',async({page})=>{
 await db.query(`CREATE OR REPLACE FUNCTION test_reject_account() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'synthetic failure'; END $$ LANGUAGE plpgsql; CREATE TRIGGER test_rollback BEFORE INSERT ON "BankAccounts" FOR EACH ROW EXECUTE FUNCTION test_reject_account();`);
 try{
  await fill(page,2);const before=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
  await page.locator('#submit-button').click();await expect(page.locator('.error-summary')).toContainText('ثبت اطلاعات انجام نشد');
  await expect(page.locator('[name="Input.Accounts[0].IBAN"]')).toHaveValue(iban);
  expect(+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count).toBe(before);
 }finally{await db.query('DROP TRIGGER test_rollback ON "BankAccounts"; DROP FUNCTION test_reject_account();');}
});
const applicants=async()=>+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
const accountsOf=async firstName=>+(await db.query('SELECT count(*) FROM "BankAccounts" b JOIN "Applicants" a ON a."Id"=b."ApplicantId" WHERE a."FirstName"=$1',[firstName])).rows[0].count;
test('شبای تکراری رد می‌شود و پیغام تکراری می‌دهد',async({page})=>{
 await fill(page);const registered=iban;await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 const before=await applicants();
 await fill(page,1,[registered]);await page.locator('#submit-button').click();
 await expect(page.locator('.error-summary')).toContainText('تکراری');
 await expect(page.locator('[data-valmsg-for="Input.Accounts[0].IBAN"]')).toContainText('قبلاً ثبت شده است');
 expect(await applicants()).toBe(before);
});
test('شبای تکراری در یک فرم رد می‌شود',async({page})=>{
 const before=await applicants();const same=newIban();
 await fill(page,2,[same,same]);await page.locator('#submit-button').click();
 await expect(page.locator('[data-valmsg-for="Input.Accounts[1].IBAN"]')).toContainText('تکراری');
 expect(await applicants()).toBe(before);
});
test('ثبت دوباره با شبای قبلی و شبای جدید، حساب جدید را به همان فرد اضافه می‌کند',async({page})=>{
 const firstName=await fill(page);const registered=iban;await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 const before=await applicants();
 await fill(page,2,[registered]);await page.locator('[name="Input.FirstName"]').fill(firstName);
 await page.locator('[name="Input.Mobile"]').fill('09120000000');
 await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 expect(await applicants()).toBe(before);expect(await accountsOf(firstName)).toBe(2);
 expect((await db.query('SELECT "Mobile" FROM "Applicants" WHERE "FirstName"=$1',[firstName])).rows[0].Mobile).toBe('09120000000');
 // The same known IBAN under another name is not merged into the first person's record.
 await fill(page,2,[registered]);await page.locator('#submit-button').click();
 await expect(page.locator('[data-valmsg-for="Input.Accounts[0].IBAN"]')).toContainText('قبلاً ثبت شده است');
 expect(await applicants()).toBe(before);expect(await accountsOf(firstName)).toBe(2);
});
test('دسترسی مدیر، ماسک، خروجی CSV و خروج',async({page})=>{
 const second=newIban(),firstName=await fill(page,2,[undefined,second]);
 await page.locator('[name="Input.Mobile"]').fill('09123456789');
 await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 await page.goto('/admin');await expect(page).toHaveURL(/\/admin\/login/);
 await expect(page.locator('[name="Email"]')).toHaveCount(0);await page.locator('[name="Password"]').fill(secrets.password);
 await page.locator('button[type=submit]').click();await expect(page).toHaveURL(/\/admin$/);
 const html=await page.content();expect(html).not.toContain(iban);
 // Exports include every applicant, even when the active search has no matching rows.
 await page.goto('/admin?PageNumber=2&Search='+encodeURIComponent('بدون نتیجه '+Date.now()));
 await expect(page.locator('.empty-state')).toBeVisible();
 const totalApplicants=+(await db.query('SELECT count(*) FROM "Applicants"')).rows[0].count;
 const downloadPromise=page.waitForEvent('download');await page.getByRole('button',{name:'خروجی CSV ماسک‌شده'}).click();
 const download=await downloadPromise;const csv=fs.readFileSync(await download.path(),'utf8');
 expect(csv.split(/\r?\n/).filter(line=>line.length)).toHaveLength(totalApplicants+1);
 expect(csv).not.toContain(iban);expect(csv).toContain('••••');expect(csv).not.toContain('CardMasked');expect(csv).not.toContain('AccountMasked');
 const masked=[firstName+' کاربر ساختگی','09123456789','بانک آزمایشی','•••• '+iban.slice(-4),'استعلام نشده','بانک آزمایشی','•••• '+second.slice(-4),'استعلام نشده'];
 expect(csv).toContain('"نام و نام خانوادگی","شماره همراه","نام بانک اول","شماره شبا اول","وضعیت استعلام اول","نام بانک دوم","شماره شبا دوم","وضعیت استعلام دوم"');
 expect(csv.split(/\r?\n/).filter(l=>l.includes(firstName))).toHaveLength(1);expect(csv).toContain(masked.map(c=>`"${c}"`).join(','));
 const excelPromise=page.waitForEvent('download');await page.getByRole('button',{name:'خروجی اکسل ماسک‌شده'}).click();
 const excel=await excelPromise;expect(excel.suggestedFilename()).toBe('accounts-masked.xlsx');
 const maskedSheet=sheetXml(fs.readFileSync(await excel.path()));expect(maskedSheet).not.toContain(iban);expect(maskedSheet).not.toContain(second);
 expect(maskedSheet.match(/<row>/g)).toHaveLength(totalApplicants+1);
 const maskedRows=maskedSheet.split('<row>').filter(r=>r.includes(firstName));expect(maskedRows).toHaveLength(1);
 expect([...maskedRows[0].matchAll(/<t[^>]*>([^<]*)<\/t>/g)].map(m=>m[1])).toEqual(masked);
 const fullPromise=page.waitForEvent('download');await page.getByRole('button',{name:'خروجی اکسل کامل'}).click();
 const full=await fullPromise;expect(full.suggestedFilename()).toBe('accounts-full.xlsx');
 const sheet=sheetXml(fs.readFileSync(await full.path()));
 expect(sheet.match(/<row>/g)).toHaveLength(totalApplicants+1);
 for(const text of ['نام و نام خانوادگی','شماره همراه','نام بانک اول','شماره شبا اول','نام بانک دوم','شماره شبا دوم'])expect(sheet).toContain(text);
 // One row per person: name, mobile, then a bank/IBAN pair for each account.
 const rows=sheet.split('<row>').filter(r=>r.includes(firstName));expect(rows).toHaveLength(1);
 const cells=[...rows[0].matchAll(/<t[^>]*>([^<]*)<\/t>/g)].map(m=>m[1]);
 expect(cells).toEqual([firstName+' کاربر ساختگی','09123456789','بانک آزمایشی',iban,'استعلام نشده','بانک آزمایشی',second,'استعلام نشده']);
 expect(sheet).not.toContain('••••');
 await page.getByRole('button',{name:'خروج',exact:true}).click();await page.goto('/admin');await expect(page).toHaveURL(/\/admin\/login/);
});
test('نتیجه استعلام فقط بانک و وضعیت حساب را با رنگ درست نشان می‌دهد',async({page})=>{
 const firstName=await fill(page,2);
 await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
 await page.goto('/admin/login');await page.locator('[name="Password"]').fill(secrets.password);
 await page.locator('button[type=submit]').click();await expect(page).toHaveURL(/\/admin$/);
 await page.goto('/admin?Search='+encodeURIComponent(firstName));
 await page.route('**/admin?handler=Inquire',route=>route.fulfill({json:{complete:true,accounts:[
  {registeredBank:'بانک اول',result:{success:true,correct:true,nameMatches:true,status:'صحت اطلاعات',ownerName:'نامی که نباید نمایش یابد',bankName:'بانک اول',active:true}},
  {registeredBank:'بانک دوم',result:{success:true,correct:false,nameMatches:false,status:'اطلاعات اشتباه',ownerName:'صاحب اصلی ساختگی',bankName:'بانک دوم',active:false}}
 ]}}));
 await page.locator('.inquiry-form button').click();
 const correct=page.locator('.inquiry-correct'),incorrect=page.locator('.inquiry-incorrect');
 await expect(correct).toHaveText('بانک: بانک اولوضعیت حساب: فعال');
 await expect(incorrect).toHaveText('بانک: بانک دوموضعیت حساب: غیرفعال');
 await expect(correct.locator('p')).toHaveCount(2);await expect(incorrect.locator('p')).toHaveCount(2);
 const owner=page.locator('tbody tr td').first().locator('.account-owner-name');
 await expect(owner).toHaveText('نام اصلی صاحب حساب: صاحب اصلی ساختگی');
 await expect(owner).toHaveCSS('font-size','10px');
 await expect(owner).toHaveCSS('color','rgb(138, 150, 164)');
 await expect(correct.locator('p').first()).toHaveCSS('color','rgb(22, 101, 52)');
 await expect(incorrect.locator('p').first()).toHaveCSS('color','rgb(180, 35, 53)');
 await expect(page.locator('.inquiry-results')).not.toContainText('نامی که نباید نمایش یابد');
 await expect(page.locator('.inquiry-form button')).toBeDisabled();
});

test('استعلام همه مستقل از صفحه و جست‌وجو فقط فهرست شباهای باقیمانده را بررسی می‌کند',async({page})=>{
 await page.goto('/admin/login');await page.locator('[name="Password"]').fill(secrets.password);
 await page.locator('button[type=submit]').click();await expect(page).toHaveURL(/\/admin$/);
 await page.goto('/admin?Search='+encodeURIComponent('بدون نتیجه '+Date.now()));
 const ids=['00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002'];
 let pendingCalls=0,bankCalls=0;
 await page.route('**/admin?handler=PendingInquiries',route=>{
  pendingCalls++;return route.fulfill({json:{accountIds:pendingCalls===1?ids:[]}});
 });
 await page.route('**/admin?handler=InquireBank',route=>{
  expect(route.request().postData()).toContain(ids[bankCalls++]);
  return route.fulfill({json:{success:true}});
 });
 await page.getByRole('button',{name:'استعلام همه',exact:true}).click();
 await expect(page.locator('.bulk-inquiry-status')).toContainText('2 نتیجه ثبت شد');
 expect(bankCalls).toBe(2);
 await page.getByRole('button',{name:'استعلام همه',exact:true}).click();
 await expect(page.locator('.bulk-inquiry-status')).toHaveText('تمام شباها قبلاً استعلام شده‌اند.');
 expect(bankCalls).toBe(2);
});

test('قفل حساب مدیر بعد از پنج رمز نادرست',async({page})=>{
 try{
  for(let i=0;i<5;i++){
   await page.goto('/admin/login');const token=await page.locator('[name="__RequestVerificationToken"]').inputValue();
   const r=await page.request.post('/admin/login',{form:{Email:secrets.email,Password:'IncorrectPassword1!',__RequestVerificationToken:token},maxRedirects:0});expect(r.status()).toBe(200);
  }
  const user=(await db.query('SELECT "LockoutEnd" FROM "AspNetUsers" WHERE "Email"=$1',[secrets.email])).rows[0];expect(new Date(user.LockoutEnd).getTime()).toBeGreaterThan(Date.now());
 }finally{await db.query('UPDATE "AspNetUsers" SET "LockoutEnd"=NULL,"AccessFailedCount"=0 WHERE "Email"=$1',[secrets.email]);}
});
test('CSRF، هدرهای امنیت و عدم افشای بانکی در لاگ',async({page})=>{
 const r=await page.goto('/');expect(r.headers()['cache-control']).toContain('no-store');expect(r.headers()['x-robots-tag']).toBe('noindex, nofollow');expect(r.headers()['content-security-policy']).toContain("frame-ancestors 'none'");
 const response=await page.request.post('/',{form:{'Input.FirstName':'Test'}});expect(response.status()).toBe(400);
 for(const f of ['.local/app.stdout.log','.local/app.stderr.log'])if(fs.existsSync(f)){const log=fs.readFileSync(f,'utf8');expect(log).not.toContain(iban);}
});
test('موبایل: بدون اسکرول افقی، کنترل قابل لمس و ثبت',async({page})=>{
 await page.setViewportSize({width:390,height:844});await fill(page);
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBe(true);
 const box=await page.locator('#submit-button').boundingBox();expect(box.height).toBeGreaterThanOrEqual(44);
 await page.screenshot({path:'test-results/mobile.png',fullPage:true});
 await page.locator('#submit-button').click();await expect(page).toHaveURL(/\/Success$/);
});

