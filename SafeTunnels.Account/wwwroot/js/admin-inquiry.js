'use strict';
document.querySelectorAll('.inquiry-form').forEach(form => {
  form.addEventListener('submit', async event => {
    event.preventDefault();
    const button = form.querySelector('button');
    if (button.disabled) return;
    const output = form.parentElement.querySelector('.inquiry-results');
    button.disabled = true;
    button.textContent = 'در حال استعلام…';
    output.textContent = 'در حال دریافت اطلاعات حساب‌ها…';
    try {
      const response = await fetch(form.action, {
        method: 'POST', body: new FormData(form), credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (response.redirected) throw new Error('نشست مدیریت پایان یافته است؛ دوباره وارد شوید.');
      if (!response.ok) throw new Error(response.status === 429
        ? 'تعداد درخواست‌ها زیاد است؛ یک دقیقه دیگر تلاش کنید.' : 'استعلام انجام نشد؛ دوباره تلاش کنید.');
      const data = await response.json();
      output.replaceChildren();
      for (const account of data.accounts) {
        const card = document.createElement('div');
        const title = document.createElement('strong');
        title.textContent = account.registeredBank;
        card.append(title);
        const iban = document.createElement('p');
        const number = document.createElement('bdi');
        number.textContent = account.iban;
        iban.append('شبا: ', number);
        card.append(iban);
        const addLine = text => { const p = document.createElement('p'); p.textContent = text; card.append(p); };
        const result = account.result;
        if (result.success) {
          addLine(`صاحب شبا: ${result.ownerName}`);
          addLine(`بانک: ${result.bankName || 'نامشخص'}`);
          addLine(`وضعیت حساب: ${result.active === true ? 'فعال' : result.active === false ? 'غیرفعال' : 'نامشخص'}`);
          addLine(result.nameMatches ? 'نام با نام ثبت‌شده برابر است.' : 'نام با نام ثبت‌شده برابر نیست؛ بررسی کنید.');
          addLine('مقایسهٔ نام متنی است و تأیید هویت محسوب نمی‌شود.');
        } else addLine(result.message);
        output.append(card);
      }
      if (!data.accounts.length) output.textContent = 'حسابی برای استعلام ثبت نشده است.';
    } catch (error) {
      output.textContent = error instanceof Error && error.message !== 'Failed to fetch'
        ? error.message : 'ارتباط برقرار نشد؛ دوباره تلاش کنید.';
    } finally {
      button.disabled = false;
      button.textContent = 'استعلام';
    }
  });
});
