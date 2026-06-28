import { expect, test } from '@playwright/test'

test('renders the desktop chat shell empty state', async ({ page }) => {
  await page.goto('/chat')

  await expect(page.getByText('ConnectOnion')).toBeVisible()
  await expect(
    page.getByText('Add an agent to start a conversation.'),
  ).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add Agent' })).toBeVisible()
})

test('opens the settings route from the shell navigation', async ({ page }) => {
  await page.goto('/chat')
  await page.getByRole('link', { name: /Settings/ }).click()

  await expect(page).toHaveURL(/\/settings$/)
  await expect(page.getByRole('heading', { name: 'Settings' })).toBeVisible()
  await expect(page.getByRole('button', { name: /Dark/ })).toBeVisible()
  await expect(page.getByText('Keyboard shortcuts')).toBeVisible()
})
