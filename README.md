# Приложение для аннотации изображений
Ссылка на тз- [https://github.com/HolyCapybara1/task2/blob/main/Техническое_задание]


Инструкция по работе с приложением:
1. Запустите проект
2. Выберите "Загрузить изображение"
3. Выберите папку в проекте "ИзображенияТест"
4. Данные сохранятся в базе данных по photo.db
5. Просмотр результатов возможен через DB Browser SQlite
6. Скрипт для DB Browser SQlite :
   SELECT i.DisplayName       AS Картинка,
       q.Text              AS Вопрос,
       CASE a.Value
            WHEN 1 THEN 'Да'
            WHEN 0 THEN 'Нет'
            WHEN 2 THEN 'Не знаю'
       END                 AS Ответ,
       a.AnsweredAt        AS Дата
FROM Answers a
JOIN Images i   ON a.ImageId = i.Id
JOIN Questions q ON a.QuestionId = q.Id
ORDER BY i.Id, q.SortOrder;

Результат работы приложения:
<img width="588" height="279" alt="image" src="https://github.com/user-attachments/assets/ab798fce-bd27-4e93-abc1-32c110cd2714" />
