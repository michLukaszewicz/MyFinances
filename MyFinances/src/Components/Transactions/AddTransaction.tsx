import { useState } from "react";
import Box from "../Box/Box";
import type { Transaction } from "../../Models/Transaction";

interface Props {
  onAddTransaction: (transaction: Transaction) => void
}

const AddTransaction = ({onAddTransaction} : Props) => {
  const [form, setForm] = useState({
    date: new Date().toISOString().split("T")[0],
    description: "",
    amount: "0.0",
    category: "groceries",
    bankAccount: "account1",
    otherSideOfTransaction: "",
  });

const onSubmit = (event: React.FormEvent<HTMLFormElement>) => {
  event.preventDefault();
  onAddTransaction({...form, id: -1, date: new Date(form.date), amount: parseFloat(form.amount)})
}

  return (
         <Box header="Add Transaction">
        <form className="p-3 grid grid-cols-8 items-center gap-3" onSubmit={onSubmit}>
          <div className="flex flex-col gap-2 mb-3">
            <label htmlFor="otherSideOfTransaction">{parseFloat(form.amount) < 0 ? "To" : "From"}</label>
            <input
              autoFocus
              type="text"
              id="otherSideOfTransaction"
              placeholder="Company S.A."
              value={form.otherSideOfTransaction}
              onChange={(e) => setForm({ ...form, otherSideOfTransaction: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"></input>
          </div>
          <div className="mb-2 flex flex-col gap-2">
            <label htmlFor="description">Description</label>
            <input
              type="text"
              id="description"
              placeholder="electricity bill"
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:outline-none focus:border-blue-500 block w-full p-2.5"></input>
          </div>
          <div className="mb-2 flex flex-col gap-2">
            <label htmlFor="date">Date</label>
            <input
              type="date"
              id="date"
              value={form.date}
              onChange={(e) => setForm({ ...form, date: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block p-2.5"></input>
          </div>
          <div className="mb-2 flex flex-col gap-2">
            <label htmlFor="amount">Amount</label>
            <input
              type="number"
              step="0.01"
              id="amount"
              placeholder="123.45 PLN"
              value={form.amount}
              onChange={(e) => setForm({ ...form, amount: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5"></input>
          </div>
          <div className="mb-2 flex flex-col gap-2">
            <label htmlFor="category">Category</label>
            <select
              id="category"
              name="category"
              value={form.category}
              onChange={(e) => setForm({ ...form, category: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5">
              <option value={"groceries"}>Groceries</option>
              <option value={"rent"}>Rent</option>
              <option value={"bills"}>Bills</option>
              <option value={"otherHousehold"}>Other Household</option>
              <option value={"salary"}>Salary</option>
              <option value={"entertainment"}>Entertainment</option>
            </select>
          </div>
          <div className="mb-2 flex flex-col gap-2">
            <label htmlFor="bankAccount">Bank Account</label>
            <select
              id="bankAccount"
              name="bankAccount"
              value={form.bankAccount}
              onChange={(e) => setForm({ ...form, bankAccount: e.target.value })}
              className="bg-gray-50 border border-gray-300 text-gray-900 text-sm rounded-md focus:border-blue-500 block w-full p-2.5">
              <option value={"account1"}>Account 1</option>
              <option value={"account2"}>Account 2</option>
            </select>
          </div>
          <div className="mt-6">
            <button
              type="submit"
              className="w-full py-2 px-4 bg-blue-600 text-white rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2">
              Add
            </button>
          </div>
        </form>
      </Box>
  );
};

export default AddTransaction;
