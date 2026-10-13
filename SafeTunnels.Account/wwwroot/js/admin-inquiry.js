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
    let complete = false;
    let correct = false;
    try {
      const response = await fetch(form.action, {
        method: 'POST', body: new FormData(form), credentials: 'same-origin',
        headers: { Accept: 'application/json' }
      });
      if (response.redirected) throw new Error('نشست مدیریت پایان یافته است؛ دوباره وارد شوید.');
      if (!response.ok) throw new Error(response.status === 429
        ? 'تعداد درخواست‌ها زیاد است؛ یک دقیقه دیگر تلاش کنید.' : 'استعلام انجام نشد؛ دوباره تلاش کنید.');
      const data = await response.json();
      complete = data.complete === true;
      correct = complete && data.accounts.length > 0 && data.accounts.every(account =>
        account.result.correct === true);
      output.replaceChildren();
      for (const account of data.accounts) {
        const card = document.createElement('div');
        const addLine = text => { const p = document.createElement('p'); p.textContent = text; card.append(p); };
        const result = account.result;
        if (result.success) {
          card.className = result.correct ? 'inquiry-correct' : 'inquiry-incorrect';
          card.setAttribute('aria-label', result.status);
          addLine(`بانک: ${result.bankName || account.registeredBank}`);
          addLine(`وضعیت حساب: ${result.active === true ? 'فعال' : result.active === false ? 'غیرفعال' : 'نامشخص'}`);
        } else {
          addLine(`بانک: ${account.registeredBank}`);
          addLine(result.message);
        }
        output.append(card);
      }
      if (!data.accounts.length) output.textContent = 'حسابی برای استعلام ثبت نشده است.';
    } catch (error) {
      output.textContent = error instanceof Error && error.message !== 'Failed to fetch'
        ? error.message : 'ارتباط برقرار نشد؛ دوباره تلاش کنید.';
    } finally {
      button.disabled = complete;
      button.textContent = correct ? 'اطلاعات صحیح است' : complete ? 'استعلام ثبت شده' : 'استعلام همه شباها';
    }
  });
});
